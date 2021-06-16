module ToDoList.Transaction

open Giraffe
open FSharp.Control.Tasks
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Data.SqlClient
open Microsoft.Extensions.Configuration
open Exceptions

type TransactionPayload = (SqlConnection * SqlTransaction)
type TransactionFunction<'a, 'b> =  TransactionPayload -> HttpContext -> Task<Result<'a, 'b>>

let withTransaction<'a> = fun (f: TransactionFunction<'a, exn>) (ctx: HttpContext) ->
    task {
        let config = ctx.GetService<IConfiguration>()
        let connectionStringFromConfig = config.["ConnectionString:DefaultConnectionString"]
        use connectionString = new SqlConnection(connectionStringFromConfig)
        do! connectionString.OpenAsync()
        use trans = connectionString.BeginTransaction()
        try
            let! res = f (connectionString, trans) ctx
            do! trans.CommitAsync()
            do! connectionString.CloseAsync()
            match res with
                | Ok a -> return a |> Ok
                | Error exp ->
                   return exp |> Error
        with ex ->
            do! trans.RollbackAsync()
            return Error ex
    }
    

let transaction = fun f next ctx ->
    task {
        match! withTransaction f ctx with
            | Ok res -> return! json (jsonApiWrap res) next ctx
            | Error exp -> return! handleErrorJsonAPI exp next ctx
    }