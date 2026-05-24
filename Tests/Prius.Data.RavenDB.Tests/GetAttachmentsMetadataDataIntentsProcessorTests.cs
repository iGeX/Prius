using Xunit;
using Prius.Engine.Abstractions;
using System.Text;

namespace Prius.Data.RavenDB.Tests;

public class GetAttachmentsMetadataDataIntentsProcessorTests : AbstractDataIntentsProcessorTests
{
    [Fact]
    public async Task ShouldGetAttachmentsMetadata()
    {
        const string DocId = "users/1";
        
        using var store = GetDocumentStore();
        using (var session = store.OpenAsyncSession())
        {
            await session.StoreAsync(new { Name = "John" }, DocId, TestContext.Current.CancellationToken);
            session.Advanced.Attachments.Store(DocId, "avatar.png", new MemoryStream(Encoding.UTF8.GetBytes("fake-png")), "image/png");
            session.Advanced.Attachments.Store(DocId, "resume.pdf", new MemoryStream(Encoding.UTF8.GetBytes("fake-pdf")), "application/pdf");
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var context = new MockReactorContext();
        var provider = new MockDataIntentsProvider
        {
            GetAttachmentsMetadata = [new GetAttachmentsMetadataIntent(context, DocId, "output/metadata", "failures/1", TestContext.Current.CancellationToken)]
        };

        await ExecuteTest(store, provider, () =>
        {
            Assert.True(context.PutCalls.ContainsKey("output/metadata"));
            var metadata = context.PutCalls["output/metadata"].AsMap();
            
            var avatarValue = metadata["avatar.png"];
            Assert.False(avatarValue.IsEmpty);
            var avatarMeta = avatarValue.AsMap();
            Assert.Equal("image/png", avatarMeta["ContentType"].AsString());
            
            var resumeValue = metadata["resume.pdf"];
            Assert.False(resumeValue.IsEmpty);
            var resumeMeta = resumeValue.AsMap();
            Assert.Equal("application/pdf", resumeMeta["ContentType"].AsString());
            
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ShouldReturnEmpty_WhenNoAttachments()
    {
        const string DocId = "users/1";
        
        using var store = GetDocumentStore();
        using (var session = store.OpenAsyncSession())
        {
            await session.StoreAsync(new { Name = "John" }, DocId, TestContext.Current.CancellationToken);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var context = new MockReactorContext();
        var provider = new MockDataIntentsProvider
        {
            GetAttachmentsMetadata = [new GetAttachmentsMetadataIntent(context, DocId, "output/metadata", "failures/1", TestContext.Current.CancellationToken)]
        };

        await ExecuteTest(store, provider, () =>
        {
            Assert.True(context.PutCalls.ContainsKey("output/metadata"));
            Assert.True(context.PutCalls["output/metadata"].AsMap().IsEmpty);
            return Task.CompletedTask;
        });
    }
}
