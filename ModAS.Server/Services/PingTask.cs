namespace ModAS.Server.Services;

public class PingTask : IHostedService, IDisposable {
    public Task StartAsync(CancellationToken cancellationToken) => throw new NotImplementedException();

    public Task StopAsync(CancellationToken cancellationToken) => throw new NotImplementedException();

    public void Dispose() => throw new NotImplementedException();
}