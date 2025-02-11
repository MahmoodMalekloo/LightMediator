﻿using Microsoft.Extensions.Logging;
using System;
using System.Reflection;

namespace LightMediator;
internal class Mediator : IMediator
{
    private readonly ILogger<Mediator> _logger;
    private readonly LightMediatorOptions _mediatorOptions;
    private readonly IServiceProvider _serviceProvider;

    public IEnumerable<INotificationHandler> _notificationHandlers { get; } = new List<INotificationHandler>();
    public Mediator(IServiceProvider serviceProvider, ILogger<Mediator> logger, LightMediatorOptions mediatorOptions)
    {
        _serviceProvider = serviceProvider;
        _notificationHandlers = serviceProvider.GetServices<INotificationHandler>();
        _logger = logger;
        _mediatorOptions = mediatorOptions;
    }
    public Task Publish(INotification notification, CancellationToken? cancellationToken = null)
    {
        var eventName = _mediatorOptions.IgnoreNamespaceInAssemblies
        ? notification.GetType().Name
        : notification.GetType().FullName;

        var matchingHandlers = _notificationHandlers
            .Where(c => c.NotificationName.Equals(eventName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var handler in matchingHandlers)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await handler.HandleNotification(notification, _mediatorOptions, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error in handler: {ex.Message}");
                }
            });
        }
        return Task.CompletedTask;
    }

    public async Task Send<TRequest>(TRequest request, CancellationToken? cancellationToken = null) where TRequest : class, IRequest
    {
        if (request == null)
            throw new ArgumentNullException("request is null");

        var requestName = _mediatorOptions.IgnoreNamespaceInAssemblies
                          ? request.GetType().Name
                          : request.GetType().FullName;

        List<Type> requestTypes = new List<Type>();

        foreach (var assembly in _mediatorOptions.Assemblies)
        {

            requestTypes.AddRange(assembly.GetTypes()
                .Where(type => !type.IsAbstract &&
                               !type.IsInterface &&
                               (_mediatorOptions.IgnoreNamespaceInAssemblies &&
                                    type.Name == requestName) ||
                                    (!_mediatorOptions.IgnoreNamespaceInAssemblies &&
                                    type.FullName == requestName))
                .ToList());
        }
        using var scope = _serviceProvider.CreateScope();
        foreach (Type requestType in requestTypes)
        {
            var handlerType = typeof(IRequestHandler<>).MakeGenericType(requestType);
            if (handlerType != null)
            {
                var handler = scope.ServiceProvider.GetService(handlerType);

                if (handler != null)
                {
                    var handleMethod = handlerType.GetMethod("HandleRequest");
                    var requestInstance = Activator.CreateInstance(requestType);
                    if (requestInstance == null)
                        throw new ArgumentNullException("request is null");
                    var task = (Task)handleMethod!.Invoke(handler, new object[] { requestInstance, _mediatorOptions, CancellationToken.None })!;

                    await task; // Ensure async execution
                    return;
                }
            }
        }
    }

    public async Task<TResponse?> Send<TResponse>(IRequest<TResponse> request, CancellationToken? cancellationToken = null)
    {
        if (request == null)
            throw new ArgumentNullException("request is null");

        var requestName = _mediatorOptions.IgnoreNamespaceInAssemblies
       ? request.GetType().Name
       : request.GetType().FullName;

        List<Type> requestTypes = new List<Type>();

        foreach (var assembly in _mediatorOptions.Assemblies)
        {

            requestTypes.AddRange(assembly.GetTypes()
                .Where(type => !type.IsAbstract &&
                               !type.IsInterface &&
                               (_mediatorOptions.IgnoreNamespaceInAssemblies &&
                                    type.Name == requestName) ||
                                    (!_mediatorOptions.IgnoreNamespaceInAssemblies &&
                                    type.FullName == requestName))
                .ToList());
        }
        using var scope = _serviceProvider.CreateScope();
        foreach (Type requestType in requestTypes)
        {
            Type? responseType = requestType.GetInterfaces()!.FirstOrDefault()!.GetGenericArguments()!.FirstOrDefault();
            if (responseType == null)
                continue;
            var handlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, responseType);
            if (handlerType != null)
            {
                var handler = scope.ServiceProvider.GetService(handlerType);

                if (handler != null)
                {
                    var handleMethod = handlerType.GetMethod("HandleRequest");
                    var requestInstance = Activator.CreateInstance(requestType);
                    if (requestInstance == null)
                        throw new ArgumentNullException("request is null");

                    var task = (Task)handleMethod!.Invoke(handler, new object[] { requestInstance, _mediatorOptions, CancellationToken.None })!;
                     
                    object result = await ConvertToGenericTask(task, responseType);

                    return JsonConvert.DeserializeObject<TResponse>(JsonConvert.SerializeObject(result));
               
                }
            }
        }
        return default;
    }
    static async Task<object> ConvertToGenericTask(Task task, Type responseType)
    { 
        var genericTaskType = typeof(Task<>).MakeGenericType(responseType);
        var resultProperty = genericTaskType.GetProperty("Result");
         
        await task.ConfigureAwait(false);
        return resultProperty!.GetValue(task)!;
    }
}
