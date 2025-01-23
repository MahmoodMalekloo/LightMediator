using ServiceC.BackService.Application;
using System.Reflection;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();

// Add mediator to dc
builder.Services.AddAppMediator();
// Use extension method to register types from projects
builder.Services.RegisterNotificationsFromAssemblies(
                                Assembly.GetExecutingAssembly(),
                                 ServiceA.BackService.Extensions.ServiceExtensions.GetServiceAssembly(),
                                 ServiceB.BackService.Extensions.ServiceExtensions.GetServiceAssembly(),
                                 ServiceC.BackService.Extensions.ServiceExtensions.GetServiceAssembly()
                            );

// Use hosting extension of services to configure it
builder.Services.AddServiceA();
builder.Services.AddServiceB();
builder.Services.AddServiceC();

var host = builder.Build();

host.Run();
