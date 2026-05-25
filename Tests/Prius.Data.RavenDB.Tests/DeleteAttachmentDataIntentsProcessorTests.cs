using Xunit;
using Prius.Engine.Abstractions;
using System.Text;

namespace Prius.Data.RavenDB.Tests;

public class DeleteAttachmentDataIntentsProcessorTests : AbstractDataIntentsProcessorTests
{
    [Fact]
    public async Task ShouldDeleteAttachment()
    {
        const string DocId = "users/1";
        const string AttachmentName = "avatar.png";
        var content = Encoding.UTF8.GetBytes("fake-image-content");
        
        using var store = GetDocumentStore();
        using (var session = store.OpenAsyncSession())
        {
            await session.StoreAsync(new { Name = "John" }, DocId, TestContext.Current.CancellationToken);
            session.Advanced.Attachments.Store(DocId, AttachmentName, new MemoryStream(content), "image/png");
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var context = new MockElementContext();
        var provider = new MockDataIntentsProvider
        {
            DeleteAttachments = [new DeleteAttachmentIntent(context, DocId, AttachmentName, "success/1", "failures/1", TestContext.Current.CancellationToken)]
        };

        await ExecuteTest(store, provider, async () =>
        {
            Assert.True(context.PutCalls.ContainsKey("success/1"));
            
            using var session = store.OpenAsyncSession();
            var exists = await session.Advanced.Attachments.ExistsAsync(DocId, AttachmentName, TestContext.Current.CancellationToken);
            Assert.False(exists);
        });
    }
}
