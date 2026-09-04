using Ligature.Platform.Application.Abstractions;
using Ligature.SharedKernel.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Ligature.Platform.Application.Dispatching;

public sealed class CommandDispatcher : ICommandDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly CommandPipeline _pipeline;

    public CommandDispatcher(
        IServiceProvider serviceProvider,
        CommandPipeline pipeline)
    {
        _serviceProvider = serviceProvider;
        _pipeline = pipeline;
    }

    public Task<TResult> SendAsync<TCommand, TResult>(
        TCommand command,
        CancellationToken cancellationToken)
        where TCommand : ICommand<TResult>
    {
        var handler =
            _serviceProvider
                .GetRequiredService<
                    ICommandHandler<TCommand, TResult>>();

        var behaviors =
            _serviceProvider
                .GetServices<
                    ICommandBehavior<TCommand, TResult>>();

        return _pipeline.ExecuteAsync(
            command,
            behaviors.ToList(),
            handler,
            cancellationToken);
    }
}