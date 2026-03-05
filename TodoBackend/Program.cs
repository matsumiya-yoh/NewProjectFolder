using TodoBackend.Models;
using TodoBackend.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddSingleton<TodoService>();

var app = builder.Build();
app.UseCors();

// ==================================================
// 💡 全体・共通系のAPI
// ==================================================
app.MapGet("/api/insights", (TodoService service) => Results.Ok(service.GetGlobalInsights()));
app.MapGet("/api/todos", (TodoService service) => Results.Ok(service.GetAllTodos()));

// ==================================================
// 💡 日付ごとのAPIグループ (/api/todo/{date})
// ==================================================
var todoApi = app.MapGroup("/api/todo/{date}");

todoApi.AddEndpointFilter(async (context, next) =>
{
    Console.WriteLine($"[Log] {context.HttpContext.Request.Method} {context.HttpContext.Request.Path}");
    return await next(context); 
});

todoApi.MapGet("/", (string date, string? query, bool? hideCompleted, TodoService service) => 
    Results.Ok(service.GetTodos(date, query, hideCompleted ?? false)));

todoApi.MapPost("/", (string date, TodoTask task, TodoService service) => {
    service.Add(date, task);
    return Results.Created($"/api/todo/{date}", task);
});

todoApi.MapPut("/{id:int}", (string date, int id, TaskUpdateRequest req, TodoService service) => {
    service.UpdateTask(date, id, req);
    return Results.Ok();
});

todoApi.MapGet("/stats", (string date, string? userName, TodoService service) => 
    Results.Ok(service.GetStats(date, userName)));

todoApi.MapPut("/toggle/{id:int}", (string date, int id, TodoService service) => {
    service.Toggle(date, id);
    return Results.NoContent();
});

todoApi.MapDelete("/{id:int}", (string date, int id, TodoService service) => {
    service.Delete(date, id);
    return Results.NoContent(); 
});

todoApi.MapPut("/{id:int}/time", (string date, int id, TimeUpdateRequest req, TodoService service) => {
    // 💡 変更：引数をStartTime/EndTimeに対応
    service.UpdateTime(date, id, req.StartTime, req.EndTime, req.ActualTime);
    return Results.NoContent();
});

todoApi.MapPost("/{id:int}/carryover", (string date, int id, TodoService service) => {
    service.CarryOver(date, id);
    return Results.NoContent();
});

// ==================================================
// 💡 動的カテゴリー・工数関連のAPI
// ==================================================
app.MapGet("/api/categories", (TodoService service) => Results.Ok(service.GetCategories()));
app.MapPost("/api/categories/{name}", (string name, TodoService service) => { service.AddCategory(name); return Results.Ok(); });
app.MapDelete("/api/categories/{name}", (string name, TodoService service) => { service.DeleteCategory(name); return Results.NoContent(); });
app.MapPost("/api/timesheet", (List<string> dates, TodoService service) => { return Results.Ok(service.GetTimesheet(dates)); });

// ==================================================
// 💡 グループ（よく使うメンバー）管理のAPI
// ==================================================
app.MapGet("/api/groups/{userName}", (string userName, TodoService service) => 
    Results.Ok(service.GetUserGroups(userName)));

app.MapPost("/api/groups/{userName}", (string userName, UserGroup group, TodoService service) => {
    service.AddUserGroup(userName, group);
    return Results.Ok();
});

app.MapDelete("/api/groups/{userName}/{groupName}", (string userName, string groupName, TodoService service) => {
    service.DeleteUserGroup(userName, groupName);
    return Results.Ok();
});

// ==================================================
// 💡 新規追加：部屋（Room）管理のAPI
// ==================================================
app.MapGet("/api/rooms", (TodoService service) => Results.Ok(service.GetRooms()));
app.MapPost("/api/rooms/{name}", (string name, TodoService service) => { service.AddRoom(name); return Results.Ok(); });
app.MapDelete("/api/rooms/{name}", (string name, TodoService service) => { service.DeleteRoom(name); return Results.NoContent(); });


app.Run();