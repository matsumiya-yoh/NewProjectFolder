using TodoBackend.Models;
using TodoBackend.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddSingleton<TodoService>();

var app = builder.Build();
app.UseCors();

// ==================================================
// 全体・共通系のAPI
// ==================================================
app.MapGet("/api/insights", (TodoService service) => Results.Ok(service.GetGlobalInsights()));
app.MapGet("/api/todos", (TodoService service) => Results.Ok(service.GetAllTodos()));

app.MapGet("/api/todos/range", (string start, string end, string? userName, string? priorityCategory, TodoService service) => 
    Results.Ok(service.GetTodosByRange(start, end, userName, priorityCategory)));

app.MapPost("/api/todos/matrix", (MatrixRequest req, TodoService service) => 
    Results.Ok(service.GetCompanyMatrix(req.StartDate, req.EndDate, req.TeamMembers, req.PriorityCategory)));

// 💡 追加：指定した複数の日付へ一気にタスクを登録するAPI
app.MapPost("/api/todos/bulk", (BulkTodoRequest req, TodoService service) => {
    service.AddBulk(req.Dates, req.Task);
    return Results.Ok();
});

app.MapGet("/api/stats/range", (string start, string end, string? userName, TodoService service) => 
    Results.Ok(service.GetStatsByRange(start, end, userName)));

app.MapGet("/api/export/timesheet", (string start, string end, string viewMode, string? userName, TodoService service) => {
    var csvString = service.GenerateTimesheetCsv(start, end, viewMode, userName);
    var bytes = System.Text.Encoding.UTF8.GetBytes(csvString);
    var bomBytes = new byte[] { 0xEF, 0xBB, 0xBF }; 
    var finalBytes = bomBytes.Concat(bytes).ToArray();
    return Results.File(finalBytes, "text/csv", $"Timesheet_{start}_to_{end}.csv");
});

// ==================================================
// 日付ごとのAPIグループ (/api/todo/{date})
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
    service.UpdateTime(date, id, req.StartTime, req.EndTime, req.ActualTime);
    return Results.NoContent();
});

todoApi.MapPost("/{id:int}/carryover", (string date, int id, TodoService service) => {
    service.CarryOver(date, id);
    return Results.NoContent();
});

// ==================================================
// 動的カテゴリー・工数関連のAPI
// ==================================================
app.MapGet("/api/categories", (TodoService service) => Results.Ok(service.GetCategories()));
app.MapPost("/api/categories/{name}", (string name, TodoService service) => { service.AddCategory(name); return Results.Ok(); });
app.MapDelete("/api/categories/{name}", (string name, TodoService service) => { service.DeleteCategory(name); return Results.NoContent(); });
app.MapPost("/api/timesheet", (List<string> dates, TodoService service) => { return Results.Ok(service.GetTimesheet(dates)); });

// ==================================================
// タスクテンプレート管理のAPI 
// ==================================================
app.MapGet("/api/templates/{userName}", (string userName, TodoService service) => 
    Results.Ok(service.GetTemplates(userName)));

app.MapPost("/api/templates/{userName}", (string userName, TaskTemplate tmpl, TodoService service) => {
    service.AddTemplate(userName, tmpl);
    return Results.Ok();
});

// 💡 変更：共有テンプレかどうかを判別するため isShared を受け取る
app.MapDelete("/api/templates/{userName}/{templateName}", (string userName, string templateName, bool? isShared, TodoService service) => {
    service.DeleteTemplate(userName, templateName, isShared ?? false);
    return Results.Ok();
});

// ==================================================
// 部屋（Room）管理のAPI
// ==================================================
app.MapGet("/api/rooms", (TodoService service) => Results.Ok(service.GetRooms()));
app.MapPost("/api/rooms/{name}", (string name, TodoService service) => { service.AddRoom(name); return Results.Ok(); });
app.MapDelete("/api/rooms/{name}", (string name, TodoService service) => { service.DeleteRoom(name); return Results.NoContent(); });

app.Run();