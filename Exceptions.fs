module ToDoList.Exceptions

open System
open System.Collections.Generic
open System.Data
open System.Reflection
open FSharp.Data
open JsonApiSerializer
open JsonApiSerializer.JsonApi
open Microsoft.AspNetCore.Http
open Giraffe
open FSharp.Control.Tasks
open Microsoft.Data.SqlClient
open Microsoft.Extensions.Logging
open Newtonsoft.Json

type RestException(code, message) =
    inherit Exception(message)
    member __.Code = code
    
let RestExceptionResult code message = RestException(code, message) :> Exception

let BadRequestResult = RestExceptionResult HttpStatusCodes.BadRequest
let NotFoundRequestResult = RestExceptionResult HttpStatusCodes.NotFound

let createJsonApiError = fun message code ->
    let error = Error()
    error.Detail <- message
    error.Code <- string code
    
    let errors = [error]
    
    let root = DocumentRoot<obj>()
    root.Errors <- ResizeArray<Error> errors
    root

let handleErrorJsonAPI = fun (ex: Exception) _ (ctx: HttpContext) ->
    task {
        let (code, message) =
             match ex with
                | :? InvalidOperationException -> (StatusCodes.Status404NotFound, ex.Message)
                | :? KeyNotFoundException -> (StatusCodes.Status404NotFound, "")
                | :? RestException ->
                    let restEx = ex :?> RestException
                    let  message = if restEx.Code = StatusCodes.Status404NotFound then "The resource was not found." else restEx.Message
                    (restEx.Code, message)
                | :? UnauthorizedAccessException -> (StatusCodes.Status401Unauthorized, "")
                | :? BadHttpRequestException -> (StatusCodes.Status400BadRequest, "")
                | :? ArgumentException -> (StatusCodes.Status400BadRequest, ex.Message)
                | :? DBConcurrencyException -> (StatusCodes.Status409Conflict, "This record has already been updated. Please try again.")
                | :? SqlException  -> (StatusCodes.Status409Conflict, "Connectivity problem, please contact the support")
                | _ -> (StatusCodes.Status500InternalServerError, "Internal server error")
        
        let root = createJsonApiError message code
        
        let result = JsonConvert.SerializeObject(root, JsonApiSerializerSettings())
        ctx.SetStatusCode code
        ctx.SetContentType HttpContentTypes.Json
        do! ctx.Response.WriteAsync(result)
        
        return! earlyReturn ctx
    }
let createResponse = fun status message ->
    setStatusCode status >=> (json <| createJsonApiError message status)

type ErrorMessage = string
let notFound: (ErrorMessage -> HttpHandler) = createResponse HttpStatusCodes.NotFound
let forbidden: (ErrorMessage -> HttpHandler) = createResponse HttpStatusCodes.Forbidden

let jsonApiWrap<'a> = fun (data: 'a)  ->
    let result = DocumentRoot<'a>()
    
    result.Data <- data
    let versionInfo = VersionInfo()
    versionInfo.Version <- Assembly.GetEntryAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion
    
    result.JsonApi <- versionInfo
    result


let mapResultOption = fun f result ex ->
    match result with
        | Some m -> f m |> Ok
        | None -> ex |> Result.Error

let resultOptionNoMap result ex = mapResultOption id result ex
let resultOption result f ex = mapResultOption f result ex

let resultOrNotFound = fun result ->
    let notFound = RestException(StatusCodes.Status404NotFound, "") :> Exception
    resultOptionNoMap result notFound

let mapResultOrNotFound = fun result f ->
    let notFound = RestException(StatusCodes.Status404NotFound, "") :> Exception
    resultOption result f notFound

let jsonApiWrapHandler = fun result (next: HttpFunc) (ctx: HttpContext) ->
    match result with
          | Ok a -> json (jsonApiWrap a) next ctx
          | Error ex -> handleErrorJsonAPI ex next ctx
  