module ToDoList.Authorization

open System
open System.Text
open Microsoft.AspNetCore.Http
open Giraffe
open FSharp.Control.Tasks
open Microsoft.Extensions.Configuration


let validateBasicSchema header (ctx: HttpContext) =
     let pair = Encoding.UTF8.GetString(Convert.FromBase64String(header))
     let config = ctx.GetService<IConfiguration>()
             
     let ix = pair.IndexOf(':');
     if (ix = -1) then false
     else 
         let clientId = pair.Substring(0, ix);
         let clientSecret = pair.Substring(ix + 1);
        
         let res = (String.CompareOrdinal(clientId, config.["ClientId"]) = 0 && String.CompareOrdinal(clientSecret, config.["ClientSecret"]) = 0)
         res
        
let return401: HttpHandler = setStatusCode 401 >=> text "Unauthorized access"

let authorize = fun (next: HttpFunc) (ctx: HttpContext) ->
    task {
     let authHeader = ctx.TryGetRequestHeader "Authorization"
    
     match authHeader with
         | Some headerValue ->
             let tokens = Array.toList (headerValue.Split " ")
             
             match tokens with
                | (_::token::_) ->
                    let isTokenValid = validateBasicSchema token ctx
                    if isTokenValid then return! next ctx
                    else return! return401 next ctx
                | _ -> return! return401 next ctx
         | None -> return! return401 next ctx   
    }
    