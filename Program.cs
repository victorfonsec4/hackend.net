using HackEnd.Net;
using HackEnd.Net.Models;
using HackEnd.Net.ResponseBuffer;

using Microsoft.AspNetCore.Mvc;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

var builder = WebApplication.CreateSlimBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.AllowSynchronousIO = true;
    options.AddServerHeader = false;
});

#if DEBUG
builder.Services.AddSingleton((c) => new DatabaseService("Host=db;Port=5432;Database=rinha;User Id=admin;Password=password;MinPoolSize=40;MaxPoolSize=40;SSL Mode=Disable;NoResetOnClose=true;Enlist=false;Max Auto Prepare=4;Multiplexing=true;Write Coalescing Buffer Threshold Bytes=1000;Include Error Detail=true;"));
#else
builder.Logging.ClearProviders();
builder.Services.AddSingleton((c) => new DatabaseService("Host=db;Port=5432;Database=rinha;User Id=admin;Password=password;MinPoolSize=40;MaxPoolSize=40;SSL Mode=Disable;NoResetOnClose=true;Enlist=false;Max Auto Prepare=4;Multiplexing=true;Write Coalescing Buffer Threshold Bytes=1000;"));
#endif
var app = builder.Build();

app.MapPost("/clientes/{id}/transacoes", async ([FromRoute] int id, HttpRequest req, DatabaseService database) =>
{
    TransactionRequest? transactionReq;
    try
    {
        transactionReq = await JsonSerializer.DeserializeAsync(req.Body, TransactionRequestContext.Default.TransactionRequest);
    }
    catch (JsonException jsonEx)
    {
        return Results.UnprocessableEntity();
    }

    if (!TransactionIsValid(transactionReq))
    {
        return Results.UnprocessableEntity();
    }

    var result = await database.CreateTransaction(id, transactionReq);

    return result.ResultCode switch
    {
        0 => Results.Stream(stream: ResponsePooledBuffers.GetTransactionResponseStream(result.Response!), contentType: "application/json"),
        1 => Results.NotFound(),
        2 => Results.UnprocessableEntity(),
        _ => Results.Problem(detail: "Invalid database result", statusCode: StatusCodes.Status500InternalServerError)
    };

    static bool TransactionIsValid([NotNullWhen(true)] TransactionRequest? transactionReq)
    {
        if (transactionReq == null)
        {
            return false;
        }

        if (transactionReq.valor < 1)
        {
            return false;
        }

        if (string.IsNullOrEmpty(transactionReq.descricao) || transactionReq.descricao.Length > 10)
        {
            return false;
        }

        if (transactionReq.tipo != 'd' && transactionReq.tipo != 'c')
        {
            return false;
        }

        return true;
    }
});

app.MapGet("/clientes/{id}/extrato", async ([FromRoute] int id, DatabaseService database) =>
{
    var result = await database.GetStatement(id);

    if (result.ResultCode == 0)
    {
        var response = result.Response!;
        if (response.ultimas_transacoes.Count() < 10)
            return Results.Json(result.Response!, StatementResponseContext.Default.StatementResponse);

        return Results.Stream(stream: ResponsePooledBuffers.GetBalanceResponseStream(response), contentType: "application/json");

    }
    return result.ResultCode switch
    {
        1 => Results.NotFound(),
        _ => Results.Problem(detail: "Invalid database result", statusCode: StatusCodes.Status500InternalServerError)
    };
});

app.MapGet("/wipe", async (DatabaseService database) =>
{
    await database.Wipe();

    return Results.Ok();
});

app.Run();