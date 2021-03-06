// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#if DNX451
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Mvc.ModelBinding;
using Microsoft.AspNet.Testing;
using Microsoft.AspNet.Testing.xunit;
using Moq;
using Xunit;

namespace Microsoft.AspNet.Mvc.Formatters.Xml
{
    public class XmlSerializerInputFormatterTest
    {
        public class DummyClass
        {
            public int SampleInt { get; set; }
        }

        public class TestLevelOne
        {
            public int SampleInt { get; set; }
            public string sampleString;
            public DateTime SampleDate { get; set; }
        }

        public class TestLevelTwo
        {
            public string SampleString { get; set; }
            public TestLevelOne TestOne { get; set; }
        }

        [Theory]
        [InlineData("application/xml", true)]
        [InlineData("application/*", false)]
        [InlineData("*/*", false)]
        [InlineData("text/xml", true)]
        [InlineData("text/*", false)]
        [InlineData("text/json", false)]
        [InlineData("application/json", false)]
        [InlineData("", false)]
        [InlineData("invalid", false)]
        [InlineData(null, false)]
        public void CanRead_ReturnsTrueForAnySupportedContentType(string requestContentType, bool expectedCanRead)
        {
            // Arrange
            var formatter = new XmlSerializerInputFormatter();
            var contentBytes = Encoding.UTF8.GetBytes("content");

            var modelState = new ModelStateDictionary();
            var httpContext = GetHttpContext(contentBytes, contentType: requestContentType);

            var formatterContext = new InputFormatterContext(
                httpContext,
                modelName: string.Empty,
                modelState: modelState,
                modelType: typeof(string));

            // Act
            var result = formatter.CanRead(formatterContext);

            // Assert
            Assert.Equal(expectedCanRead, result);
        }

        [Theory]
        [InlineData(typeof(Dictionary<string, object>), false)]
        [InlineData(typeof(string), true)]
        public void CanRead_ReturnsFalse_ForAnyUnsupportedModelType(Type modelType, bool expectedCanRead)
        {
            // Arrange
            var formatter = new XmlSerializerInputFormatter();
            var contentBytes = Encoding.UTF8.GetBytes("content");

            var context = GetInputFormatterContext(contentBytes, modelType);

            // Act
            var result = formatter.CanRead(context);

            // Assert
            Assert.Equal(expectedCanRead, result);
        }

        [Fact]
        public void XmlSerializer_CachesSerializerForType()
        {
            // Arrange
            var input = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<DummyClass><SampleInt>10</SampleInt></DummyClass>";
            var formatter = new TestXmlSerializerInputFormatter();
            var contentBytes = Encoding.UTF8.GetBytes(input);
            var context = GetInputFormatterContext(contentBytes, typeof(DummyClass));

            // Act
            formatter.CanRead(context);
            formatter.CanRead(context);

            // Assert
            Assert.Equal(1, formatter.createSerializerCalledCount);
        }

        [Fact]
        public void HasProperSuppportedMediaTypes()
        {
            // Arrange & Act
            var formatter = new XmlSerializerInputFormatter();

            // Assert
            Assert.True(formatter.SupportedMediaTypes
                                 .Select(content => content.ToString())
                                 .Contains("application/xml"));
            Assert.True(formatter.SupportedMediaTypes
                                 .Select(content => content.ToString())
                                 .Contains("text/xml"));
        }

        [Fact]
        public void HasProperSuppportedEncodings()
        {
            // Arrange & Act
            var formatter = new XmlSerializerInputFormatter();

            // Assert
            Assert.True(formatter.SupportedEncodings.Any(i => i.WebName == "utf-8"));
            Assert.True(formatter.SupportedEncodings.Any(i => i.WebName == "utf-16"));
        }

        [Fact]
        public async Task ReadAsync_ReadsSimpleTypes()
        {
            // Arrange
            var expectedInt = 10;
            var expectedString = "TestString";
            var expectedDateTime = XmlConvert.ToString(DateTime.UtcNow, XmlDateTimeSerializationMode.Utc);

            var input = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                                "<TestLevelOne><SampleInt>" + expectedInt + "</SampleInt>" +
                                "<sampleString>" + expectedString + "</sampleString>" +
                                "<SampleDate>" + expectedDateTime + "</SampleDate></TestLevelOne>";

            var formatter = new XmlSerializerInputFormatter();
            var contentBytes = Encoding.UTF8.GetBytes(input);
            var context = GetInputFormatterContext(contentBytes, typeof(TestLevelOne));

            // Act
            var result = await formatter.ReadAsync(context);

            // Assert
            Assert.NotNull(result);
            Assert.False(result.HasError);
            var model = Assert.IsType<TestLevelOne>(result.Model);

            Assert.Equal(expectedInt, model.SampleInt);
            Assert.Equal(expectedString, model.sampleString);
            Assert.Equal(
                XmlConvert.ToDateTime(expectedDateTime, XmlDateTimeSerializationMode.Utc),
                model.SampleDate);
        }

        [Fact]
        public async Task ReadAsync_ReadsComplexTypes()
        {
            // Arrange
            var expectedInt = 10;
            var expectedString = "TestString";
            var expectedDateTime = XmlConvert.ToString(DateTime.UtcNow, XmlDateTimeSerializationMode.Utc);
            var expectedLevelTwoString = "102";

            var input = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                        "<TestLevelTwo><SampleString>" + expectedLevelTwoString + "</SampleString>" +
                        "<TestOne><SampleInt>" + expectedInt + "</SampleInt>" +
                        "<sampleString>" + expectedString + "</sampleString>" +
                        "<SampleDate>" + expectedDateTime + "</SampleDate></TestOne></TestLevelTwo>";

            var formatter = new XmlSerializerInputFormatter();
            var contentBytes = Encoding.UTF8.GetBytes(input);
            var context = GetInputFormatterContext(contentBytes, typeof(TestLevelTwo));

            // Act
            var result = await formatter.ReadAsync(context);

            // Assert
            Assert.NotNull(result);
            Assert.False(result.HasError);
            var model = Assert.IsType<TestLevelTwo>(result.Model);

            Assert.Equal(expectedLevelTwoString, model.SampleString);
            Assert.Equal(expectedInt, model.TestOne.SampleInt);
            Assert.Equal(expectedString, model.TestOne.sampleString);
            Assert.Equal(
                XmlConvert.ToDateTime(expectedDateTime, XmlDateTimeSerializationMode.Utc),
                model.TestOne.SampleDate);
        }

        [Fact]
        public async Task ReadAsync_ReadsWhenMaxDepthIsModified()
        {
            // Arrange
            var expectedInt = 10;

            var input = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<DummyClass><SampleInt>" + expectedInt + "</SampleInt></DummyClass>";
            var formatter = new XmlSerializerInputFormatter();
            formatter.MaxDepth = 10;
            var contentBytes = Encoding.UTF8.GetBytes(input);
            var context = GetInputFormatterContext(contentBytes, typeof(DummyClass));

            // Act
            var result = await formatter.ReadAsync(context);

            // Assert
            Assert.NotNull(result);
            Assert.False(result.HasError);
            var model = Assert.IsType<DummyClass>(result.Model);
            Assert.Equal(expectedInt, model.SampleInt);
        }

        [ConditionalFact]
        // ReaderQuotas are not honored on Mono
        [FrameworkSkipCondition(RuntimeFrameworks.Mono)]
        public async Task ReadAsync_ThrowsOnExceededMaxDepth()
        {
            // Arrange
            var input = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                        "<TestLevelTwo><SampleString>test</SampleString>" +
                        "<TestOne><SampleInt>10</SampleInt>" +
                        "<sampleString>test</sampleString>" +
                        "<SampleDate>" + XmlConvert.ToString(DateTime.UtcNow, XmlDateTimeSerializationMode.Utc)
                        + "</SampleDate></TestOne></TestLevelTwo>";
            var formatter = new XmlSerializerInputFormatter();
            formatter.MaxDepth = 1;
            var contentBytes = Encoding.UTF8.GetBytes(input);
            var context = GetInputFormatterContext(contentBytes, typeof(TestLevelTwo));

            // Act & Assert
            await Assert.ThrowsAsync(typeof(InvalidOperationException), () => formatter.ReadAsync(context));
        }

        [ConditionalFact]
        // ReaderQuotas are not honored on Mono
        [FrameworkSkipCondition(RuntimeFrameworks.Mono)]
        public async Task ReadAsync_ThrowsWhenReaderQuotasAreChanged()
        {
            // Arrange
            var input = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                        "<TestLevelTwo><SampleString>test</SampleString>" +
                        "<TestOne><SampleInt>10</SampleInt>" +
                        "<sampleString>test</sampleString>" +
                        "<SampleDate>" + XmlConvert.ToString(DateTime.UtcNow, XmlDateTimeSerializationMode.Utc)
                        + "</SampleDate></TestOne></TestLevelTwo>";
            var formatter = new XmlSerializerInputFormatter();
            formatter.XmlDictionaryReaderQuotas.MaxStringContentLength = 10;
            var contentBytes = Encoding.UTF8.GetBytes(input);
            var context = GetInputFormatterContext(contentBytes, typeof(TestLevelTwo));

            // Act & Assert
            await Assert.ThrowsAsync(typeof(InvalidOperationException), () => formatter.ReadAsync(context));
        }

        [Fact]
        public void SetMaxDepth_ThrowsWhenMaxDepthIsBelowOne()
        {
            // Arrange
            var formatter = new XmlSerializerInputFormatter();

            // Act & Assert
            Assert.Throws(typeof(ArgumentException), () => formatter.MaxDepth = 0);
        }

        [Fact]
        public async Task ReadAsync_VerifyStreamIsOpenAfterRead()
        {
            // Arrange
            var input = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<DummyClass><SampleInt>10</SampleInt></DummyClass>";
            var formatter = new XmlSerializerInputFormatter();
            var contentBytes = Encoding.UTF8.GetBytes(input);
            var context = GetInputFormatterContext(contentBytes, typeof(DummyClass));

            // Act
            var result = await formatter.ReadAsync(context);

            // Assert
            Assert.NotNull(result);
            Assert.False(result.HasError);
            Assert.NotNull(result.Model);
            Assert.True(context.HttpContext.Request.Body.CanRead);
        }

        [Fact]
        public async Task ReadAsync_FallsbackToUTF8_WhenCharSet_NotInContentType()
        {
            // Arrange
            var expectedException = TestPlatformHelper.IsMono ? typeof(InvalidOperationException) :
                                                                typeof(XmlException);
            var expectedMessage = TestPlatformHelper.IsMono ?
                "There is an error in XML document." :
                "The expected encoding 'utf-8' does not match the actual encoding 'utf-16LE'.";

            var inpStart = Encoding.Unicode.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-16\"?>" +
                "<DummyClass><SampleInt>");
            byte[] inp = { 192, 193 };
            var inpEnd = Encoding.Unicode.GetBytes("</SampleInt></DummyClass>");

            var contentBytes = new byte[inpStart.Length + inp.Length + inpEnd.Length];
            Buffer.BlockCopy(inpStart, 0, contentBytes, 0, inpStart.Length);
            Buffer.BlockCopy(inp, 0, contentBytes, inpStart.Length, inp.Length);
            Buffer.BlockCopy(inpEnd, 0, contentBytes, inpStart.Length + inp.Length, inpEnd.Length);

            var formatter = new XmlSerializerInputFormatter();
            var context = GetInputFormatterContext(contentBytes, typeof(TestLevelTwo));

            // Act and Assert
            var ex = await Assert.ThrowsAsync(expectedException, () => formatter.ReadAsync(context));
            Assert.Equal(expectedMessage, ex.Message);
        }

        [Fact]
        public async Task ReadAsync_UsesContentTypeCharSet_ToReadStream()
        {
            // Arrange
            var expectedException = TestPlatformHelper.IsMono ? typeof(InvalidOperationException) :
                                                                typeof(XmlException);
            var expectedMessage = TestPlatformHelper.IsMono ?
                "There is an error in XML document." :
                "The expected encoding 'utf-16LE' does not match the actual encoding 'utf-8'.";

            var inputBytes = Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<DummyClass><SampleInt>1000</SampleInt></DummyClass>");

            var formatter = new XmlSerializerInputFormatter();

            var modelState = new ModelStateDictionary();
            var httpContext = GetHttpContext(inputBytes, contentType: "application/xml; charset=utf-16");

            var context = new InputFormatterContext(
                httpContext,
                modelName: string.Empty,
                modelState: modelState,
                modelType: typeof(TestLevelOne));

            // Act and Assert
            var ex = await Assert.ThrowsAsync(expectedException, () => formatter.ReadAsync(context));
            Assert.Equal(expectedMessage, ex.Message);
        }

        [Fact]
        public async Task ReadAsync_IgnoresBOMCharacters()
        {
            // Arrange
            var sampleString = "Test";
            var sampleStringBytes = Encoding.UTF8.GetBytes(sampleString);
            var inputStart = Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Environment.NewLine +
                "<TestLevelTwo><SampleString>" + sampleString);
            byte[] bom = { 0xef, 0xbb, 0xbf };
            var inputEnd = Encoding.UTF8.GetBytes("</SampleString></TestLevelTwo>");
            var expectedBytes = new byte[sampleString.Length + bom.Length];

            var contentBytes = new byte[inputStart.Length + bom.Length + inputEnd.Length];
            Buffer.BlockCopy(inputStart, 0, contentBytes, 0, inputStart.Length);
            Buffer.BlockCopy(bom, 0, contentBytes, inputStart.Length, bom.Length);
            Buffer.BlockCopy(inputEnd, 0, contentBytes, inputStart.Length + bom.Length, inputEnd.Length);

            var formatter = new XmlSerializerInputFormatter();
            var context = GetInputFormatterContext(contentBytes, typeof(TestLevelTwo));

            // Act
            var result = await formatter.ReadAsync(context);

            // Assert
            Assert.NotNull(result);
            Assert.False(result.HasError);
            var model = Assert.IsType<TestLevelTwo>(result.Model);
            Buffer.BlockCopy(sampleStringBytes, 0, expectedBytes, 0, sampleStringBytes.Length);
            Buffer.BlockCopy(bom, 0, expectedBytes, sampleStringBytes.Length, bom.Length);
            Assert.Equal(expectedBytes, Encoding.UTF8.GetBytes(model.SampleString));
        }

        [Fact]
        public async Task ReadAsync_AcceptsUTF16Characters()
        {
            // Arrange
            var expectedInt = 10;
            var expectedString = "TestString";
            var expectedDateTime = XmlConvert.ToString(DateTime.UtcNow, XmlDateTimeSerializationMode.Utc);

            var input = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" +
                                "<TestLevelOne><SampleInt>" + expectedInt + "</SampleInt>" +
                                "<sampleString>" + expectedString + "</sampleString>" +
                                "<SampleDate>" + expectedDateTime + "</SampleDate></TestLevelOne>";

            var formatter = new XmlSerializerInputFormatter();
            var contentBytes = Encoding.Unicode.GetBytes(input);

            var modelState = new ModelStateDictionary();
            var httpContext = GetHttpContext(contentBytes, contentType: "application/xml; charset=utf-16");
            var context = new InputFormatterContext(
                httpContext,
                modelName: string.Empty,
                modelState: modelState,
                modelType: typeof(TestLevelOne));

            // Act
            var result = await formatter.ReadAsync(context);

            // Assert
            Assert.NotNull(result);
            Assert.False(result.HasError);
            var model = Assert.IsType<TestLevelOne>(result.Model);

            Assert.Equal(expectedInt, model.SampleInt);
            Assert.Equal(expectedString, model.sampleString);
            Assert.Equal(XmlConvert.ToDateTime(expectedDateTime, XmlDateTimeSerializationMode.Utc), model.SampleDate);
        }

        private InputFormatterContext GetInputFormatterContext(byte[] contentBytes, Type modelType)
        {
            var httpContext = GetHttpContext(contentBytes);
            return new InputFormatterContext(
                httpContext,
                modelName: string.Empty,
                modelState: new ModelStateDictionary(),
                modelType: modelType);
        }

        private static HttpContext GetHttpContext(
            byte[] contentBytes,
            string contentType = "application/xml")
        {
            var request = new Mock<HttpRequest>();
            var headers = new Mock<IHeaderDictionary>();
            request.SetupGet(r => r.Headers).Returns(headers.Object);
            request.SetupGet(f => f.Body).Returns(new MemoryStream(contentBytes));
            request.SetupGet(f => f.ContentType).Returns(contentType);

            var httpContext = new Mock<HttpContext>();
            httpContext.SetupGet(c => c.Request).Returns(request.Object);
            httpContext.SetupGet(c => c.Request).Returns(request.Object);
            return httpContext.Object;
        }

        private class TestXmlSerializerInputFormatter : XmlSerializerInputFormatter
        {
            public int createSerializerCalledCount = 0;

            protected override XmlSerializer CreateSerializer(Type type)
            {
                createSerializerCalledCount++;
                return base.CreateSerializer(type);
            }
        }
    }
}
#endif
