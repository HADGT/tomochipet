using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace qlthucung.Services.chat
{
    // Performs async index load/build at startup to avoid blocking constructors.
    public class TfidfIndexInitializer : IHostedService
    {
        private readonly IServiceProvider _sp;

        public TfidfIndexInitializer(IServiceProvider sp) => _sp = sp;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using var scope = _sp.CreateScope();
            var indexer = scope.ServiceProvider.GetRequiredService<TfidfIndexer>();

            try
            {
                // Try load; if not ready, build from DB and save
                await indexer.LoadAsync();
            }
            catch
            {
                // ignore load errors - fallback to build
            }

            if (!indexer.IsReady)
            {
                try
                {
                    await indexer.BuildIndexAsync();
                    if (indexer.IsReady)
                        await indexer.SaveAsync();
                }
                catch
                {
                    // log if you have logging available
                }
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}