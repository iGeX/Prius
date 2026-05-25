using Xunit;
using Prius.Core.Maps;
using Prius.Engine;
using Prius.Engine.Abstractions;

namespace Prius.Data.RavenDB.Tests;

public class GetAttachmentsDataIntentsProcessorTests : AbstractDataIntentsProcessorTests
{
    [Fact]
    public async Task Should_Download_Attachment_And_Register_In_BinaryManager()
    {
        const string DocId = "docs/download-target";
        const string AttachmentName = "invoice.pdf";
        
        using var store = GetDocumentStore();
        
        using (var session = store.OpenAsyncSession())
        {
            var doc = new { Title = "Report" };
            await session.StoreAsync(doc, DocId, TestContext.Current.CancellationToken);
            session.Advanced.GetMetadataFor(doc)["@collection"] = "docs";
            
            var testBytes = "PDF_DUMMY_CONTENT"u8.ToArray();
            session.Advanced.Attachments.Store(DocId, AttachmentName, new MemoryStream(testBytes), "application/pdf");
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var context = new MockElementContext();
        var provider = new MockDataIntentsProvider
        {
            GetAttachments = [new GetAttachmentIntent(context, DocId, AttachmentName, "cache/binary/output", "failures", TestContext.Current.CancellationToken)]
        };

        var binaryManager = new BinaryManager();
        
        // Act
        await ExecuteTest(store, provider, binaryManager, () =>
        {
            Assert.True(context.PutCalls.ContainsKey("cache/binary/output"), "Intent should report to success path");
        
            var successMap = context.PutCalls["cache/binary/output"].AsMap();
            
            const string ExpectedBinaryPathStr = $"Attachments/{DocId}/{AttachmentName}";
            Assert.True(successMap.ContainsKey(ExpectedBinaryPathStr), "Success map must contain the generated attachment path as a key");

            var returnedMetadata = successMap[ExpectedBinaryPathStr].AsMap();
            Assert.Equal("application/pdf", returnedMetadata["ContentType"].AsString());
            Assert.True(returnedMetadata["Size"].AsLong() > 0);
        
            var binaryPath = new MapPath(ExpectedBinaryPathStr);
            var accessor = binaryManager.Get(binaryPath);
        
            Assert.True(accessor.Exists, "BinaryManager must contain the downloaded attachment in the 'Attachments' branch!");
        
            using var stream = accessor.OpenStream();
            using var reader = new StreamReader(stream);
            Assert.Equal("PDF_DUMMY_CONTENT", reader.ReadToEnd());

            return Task.CompletedTask;
        });
    }
}
