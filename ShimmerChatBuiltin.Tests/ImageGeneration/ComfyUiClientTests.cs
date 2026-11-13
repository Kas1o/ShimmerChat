using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json.Linq;
using ShimmerChatBuiltin.ImageGeneration;

namespace ShimmerChatBuiltin.Tests.ImageGeneration;

public class ComfyUiClientTests
{
    private static ComfyUiClient CreateClient(ComfyUiStubHttpHandler handler, int maxWaitSeconds = 5) =>
        new(new HttpClient(handler),
            new ComfyUiClientOptions(
                "http://comfy.test",
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(maxWaitSeconds),
                TimeSpan.FromMilliseconds(10)));

    [Fact]
    public async Task GenerateAsync_QueuesPollsAndDownloadsImage()
    {
        var historyCalls = 0;
        var handler = new ComfyUiStubHttpHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path == "/prompt")
            {
                return ComfyUiStubHttpHandler.Json(
                    """{"prompt_id":"p1","number":1,"node_errors":{}}""");
            }

            if (path == "/history/p1")
            {
                historyCalls++;
                return historyCalls == 1
                    ? ComfyUiStubHttpHandler.Json("{}")
                    : ComfyUiStubHttpHandler.Json(
                        """{"p1":{"status":{"status_str":"success","completed":true},"outputs":{"9":{"images":[{"filename":"out.png","subfolder":"","type":"output"}]}}}}""");
            }

            if (path == "/view")
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47])
                };
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                return response;
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var result = await CreateClient(handler).GenerateAsync(JObject.Parse("{}"), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Images.Should().ContainSingle();
        result.Images[0].ContentType.Should().Be("image/png");
        result.Images[0].FileName.Should().Be("out.png");
        result.Images[0].Content.Should().Equal(0x89, 0x50, 0x4E, 0x47);

        handler.RequestPaths[0].Should().Be("/prompt");
        handler.RequestPaths.Should().Contain("/history/p1");
        handler.RequestPaths.Should().Contain(p => p.StartsWith("/view?filename=out.png"));
        historyCalls.Should().Be(2);
    }

    [Fact]
    public async Task GenerateAsync_NodeErrors_FailsWithDetails()
    {
        var handler = new ComfyUiStubHttpHandler(_ => ComfyUiStubHttpHandler.Json(
            """{"prompt_id":"p1","node_errors":{"3":{"errors":["value missing"]}}}"""));

        var result = await CreateClient(handler).GenerateAsync(JObject.Parse("{}"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("some nodes are invalid");
        result.Details.Should().Contain("value missing");
    }

    [Fact]
    public async Task GenerateAsync_WorkflowError_FailsWithMessage()
    {
        var handler = new ComfyUiStubHttpHandler(_ => ComfyUiStubHttpHandler.Json(
            """{"error":{"message":"bad workflow"},"node_errors":{}}"""));

        var result = await CreateClient(handler).GenerateAsync(JObject.Parse("{}"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("bad workflow");
    }

    [Fact]
    public async Task GenerateAsync_ExecutionError_FailsWithNodeMessage()
    {
        var handler = new ComfyUiStubHttpHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/prompt")
                return ComfyUiStubHttpHandler.Json("""{"prompt_id":"p1","node_errors":{}}""");

            return ComfyUiStubHttpHandler.Json(
                """{"p1":{"status":{"status_str":"error","completed":false,"messages":[["execution_error",{"exception_message":"CUDA out of memory"}]]},"outputs":{}}}""");
        });

        var result = await CreateClient(handler).GenerateAsync(JObject.Parse("{}"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("ComfyUI execution failed.");
        result.Details.Should().Contain("CUDA out of memory");
    }

    [Fact]
    public async Task GenerateAsync_CompletedWithoutImages_Fails()
    {
        var handler = new ComfyUiStubHttpHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/prompt")
                return ComfyUiStubHttpHandler.Json("""{"prompt_id":"p1","node_errors":{}}""");

            return ComfyUiStubHttpHandler.Json(
                """{"p1":{"status":{"status_str":"success","completed":true},"outputs":{}}}""");
        });

        var result = await CreateClient(handler).GenerateAsync(JObject.Parse("{}"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("no output images");
    }

    [Fact]
    public async Task GenerateAsync_NonJsonQueueResponse_Fails()
    {
        var handler = new ComfyUiStubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>not comfyui</html>")
        });

        var result = await CreateClient(handler).GenerateAsync(JObject.Parse("{}"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("non-JSON");
    }
}
