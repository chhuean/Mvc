// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics.Tracing;
using System.Threading.Tasks;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Mvc.Abstractions;
using Microsoft.AspNet.Mvc.Core;
using Microsoft.AspNet.Mvc.Internal;
using Microsoft.AspNet.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNet.Mvc.Infrastructure
{
    public class MvcRouteHandler : IRouter
    {
        private IActionContextAccessor _actionContextAccessor;
        private IActionInvokerFactory _actionInvokerFactory;
        private IActionSelector _actionSelector;
        private ILogger _logger;
#pragma warning disable 0618
        private TelemetrySource _telemetry;
#pragma warning restore 0618

        public VirtualPathData GetVirtualPath(VirtualPathContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            EnsureServices(context.Context);

            // The contract of this method is to check that the values coming in from the route are valid;
            // that they match an existing action, setting IsBound = true if the values are OK.
            context.IsBound = _actionSelector.HasValidAction(context);

            // We return null here because we're not responsible for generating the url, the route is.
            return null;
        }

        public async Task RouteAsync(RouteContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var services = context.HttpContext.RequestServices;

            // Verify if AddMvc was done before calling UseMvc
            // We use the MvcMarkerService to make sure if all the services were added.
            MvcServicesHelper.ThrowIfMvcNotRegistered(services);
            EnsureServices(context.HttpContext);

            var actionDescriptor = await _actionSelector.SelectAsync(context);
            if (actionDescriptor == null)
            {
                _logger.LogVerbose("No actions matched the current request.");
                return;
            }

            // Replacing the route data allows any code running here to dirty the route values or data-tokens
            // without affecting something upstream.
            var oldRouteData = context.RouteData;
            var newRouteData = new RouteData(oldRouteData);

            if (actionDescriptor.RouteValueDefaults != null)
            {
                foreach (var kvp in actionDescriptor.RouteValueDefaults)
                {
                    if (!newRouteData.Values.ContainsKey(kvp.Key))
                    {
                        newRouteData.Values.Add(kvp.Key, kvp.Value);
                    }
                }
            }

            try
            {
                context.RouteData = newRouteData;

#pragma warning disable 0618
                if (_telemetry.IsEnabled("Microsoft.AspNet.Mvc.BeforeAction"))
                {
                    _telemetry.WriteTelemetry(
                        "Microsoft.AspNet.Mvc.BeforeAction",
                        new { actionDescriptor, httpContext = context.HttpContext, routeData = context.RouteData });
                }
#pragma warning restore 0618

                using (_logger.BeginScope("ActionId: {ActionId}", actionDescriptor.Id))
                {
                    _logger.LogVerbose("Executing action {ActionDisplayName}", actionDescriptor.DisplayName);

                    await InvokeActionAsync(context, actionDescriptor);
                    context.IsHandled = true;
                }
            }
            finally
            {
#pragma warning disable 0618
                if (_telemetry.IsEnabled("Microsoft.AspNet.Mvc.AfterAction"))
                {
                    _telemetry.WriteTelemetry(
                        "Microsoft.AspNet.Mvc.AfterAction",
                        new { actionDescriptor, httpContext = context.HttpContext, routeData = context.RouteData });
                }
#pragma warning restore 0618

                if (!context.IsHandled)
                {
                    context.RouteData = oldRouteData;
                }
            }
        }

        private Task InvokeActionAsync(RouteContext context, ActionDescriptor actionDescriptor)
        {
            var actionContext = new ActionContext(context.HttpContext, context.RouteData, actionDescriptor);
            _actionContextAccessor.ActionContext = actionContext;

            var invoker = _actionInvokerFactory.CreateInvoker(actionContext);
            if (invoker == null)
            {
                throw new InvalidOperationException(
                    Resources.FormatActionInvokerFactory_CouldNotCreateInvoker(
                        actionDescriptor.DisplayName));
            }

            return invoker.InvokeAsync();
        }

        private void EnsureServices(HttpContext context)
        {
            if (_actionContextAccessor == null)
            {
                _actionContextAccessor = context.RequestServices.GetRequiredService<IActionContextAccessor>();
            }

            if (_actionInvokerFactory == null)
            {
                _actionInvokerFactory = context.RequestServices.GetRequiredService<IActionInvokerFactory>();
            }

            if (_actionSelector == null)
            {
                _actionSelector = context.RequestServices.GetRequiredService<IActionSelector>();
            }

            if (_logger == null)
            {
                var factory = context.RequestServices.GetRequiredService<ILoggerFactory>();
                _logger = factory.CreateLogger<MvcRouteHandler>();
            }

#pragma warning disable 0618
            if (_telemetry == null)
            {
                _telemetry = context.RequestServices.GetRequiredService<TelemetrySource>();
            }
#pragma warning restore 0618
        }
    }
}
