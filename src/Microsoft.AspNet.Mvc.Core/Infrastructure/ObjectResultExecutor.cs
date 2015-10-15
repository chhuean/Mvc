// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Mvc.Core;
using Microsoft.AspNet.Mvc.Formatters;
using Microsoft.AspNet.Mvc.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.OptionsModel;
using Microsoft.Net.Http.Headers;

namespace Microsoft.AspNet.Mvc.Infrastructure
{
    public class ObjectResultExecutor
    {
        public ObjectResultExecutor(
            IOptions<MvcOptions> options,
            IActionBindingContextAccessor bindingContextAccessor,
            ILoggerFactory loggerFactory)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (bindingContextAccessor == null)
            {
                throw new ArgumentNullException(nameof(bindingContextAccessor));
            }

            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            DefaultFormatters =
                bindingContextAccessor.ActionBindingContext?.OutputFormatters ??
                options.Value.OutputFormatters;

            RespectBrowserAcceptHeader = options.Value.RespectBrowserAcceptHeader;
            Logger = loggerFactory.CreateLogger<ObjectResultExecutor>();
        }

        protected IList<IOutputFormatter> DefaultFormatters { get; }

        protected ILogger Logger { get; }

        protected bool RespectBrowserAcceptHeader { get; }

        public virtual Task ExecuteAsync(ActionContext context, ObjectResult result)
        {
            ValidateContentTypes(result.ContentTypes);

            var formatters = result.Formatters;
            if (formatters == null || formatters.Count == 0)
            {
                formatters = DefaultFormatters;
            }

            var formatterContext = new OutputFormatterContext()
            {
                DeclaredType = result.DeclaredType,
                HttpContext = context.HttpContext,
                Object = result.Value,
            };

            var selectedFormatter = SelectFormatter(formatterContext, result.ContentTypes, formatters);
            if (selectedFormatter == null)
            {
                // No formatter supports this.
                Logger.LogWarning("No output formatter was found to write the response.");

                context.HttpContext.Response.StatusCode = StatusCodes.Status406NotAcceptable;
                return TaskCache.CompletedTask;
            }

            Logger.LogVerbose(
                "Selected output formatter '{OutputFormatter}' and content type " +
                "'{ContentType}' to write the response.",
                selectedFormatter.GetType().FullName,
                formatterContext.SelectedContentType);

            result.OnFormatting(context);
            return selectedFormatter.WriteAsync(formatterContext);
        }

        protected virtual IOutputFormatter SelectFormatter(
            OutputFormatterContext formatterContext,
            IList<MediaTypeHeaderValue> contentTypes,
            IEnumerable<IOutputFormatter> formatters)
        {
            // Check if any content-type was explicitly set (for example, via ProducesAttribute
            // or URL path extension mapping). If yes, then ignore content-negotiation and use this content-type.
            if (contentTypes.Count == 1)
            {
                Logger.LogVerbose(
                    "Skipped content negotiation as content type '{ContentType}' is explicitly set for the response.",
                    contentTypes[0]);

                return SelectFormatterUsingAnyAcceptableContentType(formatterContext, formatters, contentTypes);
            }

            var sortedAcceptHeaderMediaTypes = GetSortedAcceptHeaderMediaTypes(formatterContext);

            IOutputFormatter selectedFormatter = null;
            if (contentTypes == null || contentTypes.Count == 0)
            {
                // Check if we have enough information to do content-negotiation, otherwise get the first formatter
                // which can write the type. Let the formatter choose the Content-Type.
                if (!sortedAcceptHeaderMediaTypes.Any())
                {
                    Logger.LogVerbose("No information found on request to perform content negotiation.");

                    return SelectFormatterNotUsingAcceptHeaders(formatterContext, formatters);
                }

                //
                // Content-Negotiation starts from this point on.
                //

                // 1. Select based on sorted accept headers.
                selectedFormatter = SelectFormatterUsingSortedAcceptHeaders(
                    formatterContext,
                    formatters,
                    sortedAcceptHeaderMediaTypes);

                // 2. No formatter was found based on Accept header. Fallback to the first formatter which can write
                // the type. Let the formatter choose the Content-Type.
                if (selectedFormatter == null)
                {
                    Logger.LogVerbose("Could not find an output formatter based on content negotiation.");

                    // Set this flag to indicate that content-negotiation has failed to let formatters decide
                    // if they want to write the response or not.
                    formatterContext.FailedContentNegotiation = true;

                    return SelectFormatterNotUsingAcceptHeaders(formatterContext, formatters);
                }
            }
            else
            {
                if (sortedAcceptHeaderMediaTypes.Any())
                {
                    // Filter and remove accept headers which cannot support any of the user specified content types.
                    // That is, confirm this result supports a more specific media type than requested e.g. OK if
                    // "text/*" requested and result supports "text/plain".
                    var filteredAndSortedAcceptHeaders = sortedAcceptHeaderMediaTypes
                        .Where(acceptHeader => contentTypes.Any(contentType => contentType.IsSubsetOf(acceptHeader)));

                    selectedFormatter = SelectFormatterUsingSortedAcceptHeaders(
                        formatterContext,
                        formatters,
                        filteredAndSortedAcceptHeaders);
                }

                if (selectedFormatter == null)
                {
                    // Either there were no acceptHeaders that were present OR
                    // There were no accept headers which matched OR
                    // There were acceptHeaders which matched but there was no formatter
                    // which supported any of them.
                    // In any of these cases, if the user has specified content types,
                    // do a last effort to find a formatter which can write any of the user specified content type.
                    selectedFormatter = SelectFormatterUsingAnyAcceptableContentType(
                        formatterContext,
                        formatters,
                        contentTypes);
                }
            }

            return selectedFormatter;
        }

        protected virtual IOutputFormatter SelectFormatterNotUsingAcceptHeaders(
            OutputFormatterContext formatterContext,
            IEnumerable<IOutputFormatter> formatters)
        {
            foreach (var formatter in formatters)
            {
                if (formatter.CanWriteResult(formatterContext, contentType: null))
                {
                    return formatter;
                }
            }

            return null;
        }

        protected virtual IOutputFormatter SelectFormatterUsingSortedAcceptHeaders(
            OutputFormatterContext formatterContext,
            IEnumerable<IOutputFormatter> formatters,
            IEnumerable<MediaTypeHeaderValue> sortedAcceptHeaders)
        {
            IOutputFormatter selectedFormatter = null;
            foreach (var contentType in sortedAcceptHeaders)
            {
                // Loop through each of the formatters and see if any one will support this
                // mediaType Value.
                selectedFormatter = formatters.FirstOrDefault(
                    formatter => formatter.CanWriteResult(formatterContext, contentType));
                if (selectedFormatter != null)
                {
                    // we found our match.
                    break;
                }
            }

            return selectedFormatter;
        }

        protected virtual IOutputFormatter SelectFormatterUsingAnyAcceptableContentType(
            OutputFormatterContext formatterContext,
            IEnumerable<IOutputFormatter> formatters,
            IEnumerable<MediaTypeHeaderValue> acceptableContentTypes)
        {
            var selectedFormatter = formatters.FirstOrDefault(
                formatter => acceptableContentTypes.Any(
                    contentType => formatter.CanWriteResult(formatterContext, contentType)));

            return selectedFormatter;
        }

        private IEnumerable<MediaTypeHeaderValue> GetSortedAcceptHeaderMediaTypes(
            OutputFormatterContext formatterContext)
        {
            var request = formatterContext.HttpContext.Request;
            var incomingAcceptHeaderMediaTypes = request.GetTypedHeaders().Accept ?? new MediaTypeHeaderValue[] { };

            // By default we want to ignore considering accept headers for content negotiation when
            // they have a media type like */* in them. Browsers typically have these media types.
            // In these cases we would want the first formatter in the list of output formatters to
            // write the response. This default behavior can be changed through options, so checking here.
            var respectAcceptHeader = true;
            if (RespectBrowserAcceptHeader == false
                && incomingAcceptHeaderMediaTypes.Any(mediaType => mediaType.MatchesAllTypes))
            {
                respectAcceptHeader = false;
            }

            var sortedAcceptHeaderMediaTypes = Enumerable.Empty<MediaTypeHeaderValue>();
            if (respectAcceptHeader)
            {
                sortedAcceptHeaderMediaTypes = SortMediaTypeHeaderValues(incomingAcceptHeaderMediaTypes)
                    .Where(header => header.Quality != HeaderQuality.NoMatch);
            }

            return sortedAcceptHeaderMediaTypes;
        }

        private void ValidateContentTypes(IList<MediaTypeHeaderValue> contentTypes)
        {
            var matchAllContentType = contentTypes?.FirstOrDefault(
                contentType => contentType.MatchesAllSubTypes || contentType.MatchesAllTypes);
            if (matchAllContentType != null)
            {
                throw new InvalidOperationException(
                    Resources.FormatObjectResult_MatchAllContentType(
                        matchAllContentType, 
                        nameof(ObjectResult.ContentTypes)));
            }
        }

        private static IEnumerable<MediaTypeHeaderValue> SortMediaTypeHeaderValues(
            IEnumerable<MediaTypeHeaderValue> headerValues)
        {
            // Use OrderBy() instead of Array.Sort() as it performs fewer comparisons. In this case the comparisons
            // are quite expensive so OrderBy() performs better.
            return headerValues.OrderByDescending(
                headerValue => headerValue,
                MediaTypeHeaderValueComparer.QualityComparer);
        }
    }
}
