using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using PSO.Auth;
using PSO.Login;
using PSO.Proto;
using Xunit;

namespace PhotonCore.Tests.Integration;

public class LoginFlowTests
{
    [Fact]
    public async Task ClientHelloReturnsWorldListOnSuccess()
    {
        var hello = new ClientHello("hero", "swordfish");

        var handler = new StubHttpMessageHandler(new
        {
            worlds = new[]
            {
                new { name = "World-1", address = "127.0.0.1", port = 12001 }
            }
        });

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };

        var metricsProbe = new MetricsProbe();

        var loginHandler = new LoginHandler(
            () => Task.FromResult<ILoginDatabase>(new FakeLoginDatabase()),
            httpClient,
            NullLogger<LoginHandler>.Instance,
            success =>
            {
                metricsProbe.Record(success);
                return Task.CompletedTask;
            });

        var result = await loginHandler.ProcessAsync(hello);

        Assert.True(result.AuthResponse.Success);
        Assert.Equal("ok", result.AuthResponse.Message);
        var world = Assert.Single(result.WorldList.Worlds);
        Assert.Equal("World-1", world.Name);
        Assert.Equal("127.0.0.1", world.Address);
        Assert.Equal((ushort)12001, world.Port);
        Assert.Equal(1, metricsProbe.SuccessCount);
        Assert.Equal(0, metricsProbe.FailureCount);
    }

    private sealed class FakeLoginDatabase : ILoginDatabase
    {
        public Task OpenAsync() => Task.CompletedTask;

        public Task<bool> VerifyPasswordAsync(string username, string password) => Task.FromResult(true);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class MetricsProbe
    {
        private int _success;
        private int _failure;

        public void Record(bool success)
        {
            if (success)
            {
                Interlocked.Increment(ref _success);
            }
            else
            {
                Interlocked.Increment(ref _failure);
            }
        }

        public int SuccessCount => Volatile.Read(ref _success);

        public int FailureCount => Volatile.Read(ref _failure);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _payload;

        public StubHttpMessageHandler(object payload)
        {
            _payload = JsonSerializer.Serialize(payload);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_payload, Encoding.UTF8, "application/json")
            };

            return Task.FromResult(response);
        }
    }
}
