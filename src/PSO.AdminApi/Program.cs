using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PSO.Auth;
using BCrypt = BCrypt.Net.BCrypt;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(async (sp) => {
    var cs = builder.Configuration.GetConnectionString("Default")!;
    var db = new Db(cs); await db.OpenAsync(); return db;
});
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin()));
var app = builder.Build();
app.UseCors();

app.MapGet("/healthz", async (Task<Db> dbTask) => {
    var db = await dbTask; var ok = await db.PingAsync(); return Results.Json(new { ok = ok == 1 });
});

app.MapPost("/v1/accounts", async (Task<Db> dbTask, CreateAccount req) => {
    var db = await dbTask;
    var hash = BCrypt.HashPassword(req.Password);
    var acct = await db.CreateAccountAsync(req.Username, hash);
    return Results.Json(new { status = "created", id = acct.Id, username = acct.Username });
});

app.Run("http://127.0.0.1:5080");
record CreateAccount(string Username, string Password);
