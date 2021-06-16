module ToDoList.App

open System
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Cors.Infrastructure
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.DependencyInjection
open Giraffe
open Authorization
open ToDoHandler
open Transaction
open Exceptions

let configureAppSettings (context: WebHostBuilderContext) (config: IConfigurationBuilder) =
    let configuration = 
        config
          .AddJsonFile("appsettings.json",false,true)
          .AddJsonFile(sprintf "appsettings.%s.json" context.HostingEnvironment.EnvironmentName ,true)
          .AddEnvironmentVariables()
          .Build()

    configuration |> ignore
    
let parsingError err = RequestErrors.BAD_REQUEST err

let webApp =
    choose [
        GET >=>
            choose [
                route "/" >=> htmlFile "./pages/index.html"
                route "/todo" >=> authorize >=> transaction getToDoListHandler
                routef "/todo/%i" <| fun id -> authorize >=> (transaction <| getToDoHandler id)
            ]
        POST >=>
            choose [
                route "/todo" >=> authorize >=> transaction createToDoHandler
            ]
        PUT >=>
            choose [
                route "/todo" >=> authorize >=> transaction updateToDoHandler 
            ]
        DELETE >=>
            choose [
                routef "/todo/%i" <| fun id -> authorize >=> (transaction <| deleteToDoHandler id)
            ]
        setStatusCode 404 >=> text "Not Found" ]

let errorHandler (ex : Exception) (logger : ILogger) =
    logger.LogError(ex, "An unhandled exception has occurred while executing the request.")
    clearResponse >=> handleErrorJsonAPI ex

let configureCors (builder : CorsPolicyBuilder) =
    builder
       .AllowAnyOrigin()
       .AllowAnyMethod()
       .AllowAnyHeader()
       |> ignore

let configureApp (app : IApplicationBuilder) =
    let env = app.ApplicationServices.GetService<IWebHostEnvironment>()
    (match env.IsDevelopment() with
    | true  ->
        app.UseDeveloperExceptionPage()
    | false ->
        app .UseGiraffeErrorHandler(errorHandler)
            .UseHttpsRedirection())
        .UseCors(configureCors)
        .UseStaticFiles()
        .UseGiraffe(webApp)

let configureServices (services : IServiceCollection) =
    services.AddCors()    |> ignore
    services.AddGiraffe() |> ignore
    Dapper.FSharp.OptionTypes.register()

let configureLogging (builder : ILoggingBuilder) =
    let filter (l : LogLevel) = l.Equals LogLevel.Error
    builder.AddFilter(filter).AddConsole().AddDebug() |> ignore

[<EntryPoint>]
let main args =
    let contentRoot = Directory.GetCurrentDirectory()
    let webRoot     = Path.Combine(contentRoot, "WebRoot")
    Host.CreateDefaultBuilder(args)
        .ConfigureWebHostDefaults(
            fun webHostBuilder ->
                webHostBuilder
                    .UseContentRoot(contentRoot)
                    .UseWebRoot(webRoot)
                    .ConfigureAppConfiguration(configureAppSettings)
                    .Configure(Action<IApplicationBuilder> configureApp)
                    .ConfigureServices(configureServices)
                    .ConfigureLogging(configureLogging)
                    |> ignore)
        .Build()
        .Run()
    0