module ToDoList.ToDoHandler

open System
open Giraffe

open Microsoft.AspNetCore.Http
open ToDoList.Transaction
open FSharp.Control.Tasks
open Dapper.FSharp
open Dapper.FSharp.MSSQL
open Exceptions

[<CLIMutable>]
type ToDo = {
    Id: int
    Name: string
    Description: string
    DueDate: DateTime
    IsDone: bool
}

[<CLIMutable>]
type ToDoDTO =
    {
        Name: string
        Description: string
        DueIn: int
    }
    member this.HasErrors() =
        if this.DueIn = 0 then Some "DueIn is required"
        else if this.Name.Length = 0  then Some "Name is required."
        else if this.Name.Length > 100  then Some "Name is too long."
        else if this.Description.Length = 0  then Some "Description is required."
        else if this.Description.Length > 500  then Some "Description is too long."
        else None
    
let getToDoById = fun id (payload: TransactionPayload) ->
    let conn, trans = payload
    task {
        let getToDoCE = select {
            table "ToDos1"
            where (eq "Id" id)
            
        }
        
        let! todo = conn.SelectAsync<ToDo>(getToDoCE, trans)
        return Seq.tryHead todo
    }
    
let getToDoList = fun (payload: TransactionPayload) ->
    let conn, trans = payload
    task {
        let getToDoCE = select {
            table "ToDos1"
        }
        
        return! conn.SelectAsync<ToDo>(getToDoCE, trans)
    }
 
let getToDoListHandler = fun payload _ ->
    task {
        let! todo = getToDoList payload
        return todo |> Ok
    }    
let getToDoHandler = fun id payload _ ->
    task {
        let! todo = getToDoById id payload
        
        return resultOrNotFound todo
    }
    
let createToDo = fun todo (payload: TransactionPayload) ->
    let conn, trans = payload
    
    task {
        let insertCE = insert {
            table "ToDos1"
            value todo
            excludeColumn "Id"
            
        }
        let! rowsAffected = conn.InsertAsync(insertCE, trans)
        
        return
            if rowsAffected = 1
            then Some rowsAffected
            else None
    }
    
let updateToDo = fun todo (payload: TransactionPayload) ->
    let conn, trans = payload
    task {
        let updateCE = update {
            table "ToDos1"
            set todo
            where (eq "Id" todo.Id)
            excludeColumn "Id"
        }
        let! rowsAffected = conn.UpdateAsync(updateCE, trans)
        
        return
            if rowsAffected = 1
            then Some todo.Id
            else None
    }
let updateToDoHandler = fun payload (ctx: HttpContext) ->
    task {
        let! todo = ctx.BindJsonAsync<ToDo>()
        
        let! update = updateToDo todo payload
        return resultOrNotFound update
    }    
let createToDoHandler = fun payload (ctx: HttpContext) ->
    task {
        let! todoDTO = ctx.BindModelAsync<ToDoDTO>()
       
        match todoDTO.HasErrors() with
        | None -> 
            let date = DateTime.UtcNow
            let newDate = date.AddHours <| float todoDTO.DueIn
            
            let todo: ToDo = { Id = 0
                               Name = todoDTO.Name
                               Description = todoDTO.Description
                               DueDate = newDate
                               IsDone = false }
            
            let! created = createToDo todo payload
            
            return resultOrNotFound created
        | Some ex ->
            let unProcessable = RestException(StatusCodes.Status422UnprocessableEntity, ex) :> Exception
            return Error unProcessable
    }

let deleteToDo = fun id (payload: TransactionPayload) ->
    let conn, trans = payload
    task {
        let deleteCE = delete {
            table "ToDos1"
            where (eq "Id" id)
        }
        
        let! rowsAffected = conn.DeleteAsync(deleteCE, trans)
        return
            if rowsAffected = 1
            then Some id
            else None
    }
    
let deleteToDoHandler = fun id payload _ ->
    task {
        let! deleted = deleteToDo id payload
        return resultOrNotFound deleted
    }
