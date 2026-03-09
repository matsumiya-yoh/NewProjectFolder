using System.Text.Json.Serialization;

namespace TodoBackend.Models;

public record TodoTask(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("deadlineStr")] string? DeadlineStr,
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("startTime")] string? StartTime, 
    [property: JsonPropertyName("endTime")] string? EndTime,     
    [property: JsonPropertyName("room")] string? Room,           
    [property: JsonPropertyName("actualTime")] int ActualTime,       
    [property: JsonPropertyName("date")] string? Date,               
    [property: JsonPropertyName("userName")] string? UserName 
);

public record TimeUpdateRequest(
    [property: JsonPropertyName("startTime")] string? StartTime, 
    [property: JsonPropertyName("endTime")] string? EndTime,
    [property: JsonPropertyName("actualTime")] int ActualTime
);

public record TaskUpdateRequest(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("startTime")] string? StartTime,
    [property: JsonPropertyName("endTime")] string? EndTime,
    [property: JsonPropertyName("room")] string? Room,
    [property: JsonPropertyName("actualTime")] int ActualTime,
    [property: JsonPropertyName("members")] List<string> Members 
);

public class CategoryInfo {
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("color")] public string Color { get; set; } = "";
}

public class TaskTemplate {
    [JsonPropertyName("templateName")] public string TemplateName { get; set; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("category")] public string Category { get; set; } = "未分類";
    [JsonPropertyName("room")] public string Room { get; set; } = "未設定";
    [JsonPropertyName("startTime")] public string StartTime { get; set; } = "";
    [JsonPropertyName("endTime")] public string EndTime { get; set; } = "";
    [JsonPropertyName("members")] public List<string> Members { get; set; } = new();
}

// 💡 追加：会社全体のマトリックス取得用リクエスト
public class MatrixRequest {
    [JsonPropertyName("startDate")] public string StartDate { get; set; } = string.Empty;
    [JsonPropertyName("endDate")] public string EndDate { get; set; } = string.Empty;
    [JsonPropertyName("teamMembers")] public List<string> TeamMembers { get; set; } = new();
    [JsonPropertyName("priorityCategory")] public string? PriorityCategory { get; set; }
}

public class TodoItem 
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("isCompleted")] public bool IsCompleted { get; set; }
    [JsonPropertyName("deadline")] public DateTime? Deadline { get; set; }
    [JsonPropertyName("category")] public string Category { get; set; } = "未分類";
    [JsonPropertyName("startTime")] public string StartTime { get; set; } = "";
    [JsonPropertyName("endTime")] public string EndTime { get; set; } = "";
    [JsonPropertyName("room")] public string Room { get; set; } = "未設定";
    [JsonPropertyName("actualTime")] public int ActualTime { get; set; } = 0;
    [JsonPropertyName("date")] public string Date { get; set; } = string.Empty;
    [JsonPropertyName("userName")] public string UserName { get; set; } = "未設定";

    public TodoItem() { }

    public TodoItem(string title, bool isCompleted, DateTime? deadline = null, string category = "未分類", string startTime = "", string endTime = "", string room = "未設定", int actTime = 0, string date = "", string userName = "未設定") 
    {
        Title = title;
        IsCompleted = isCompleted;
        Deadline = deadline;
        Category = string.IsNullOrWhiteSpace(category) ? "未分類" : category;
        StartTime = startTime ?? "";
        EndTime = endTime ?? "";
        Room = string.IsNullOrWhiteSpace(room) ? "未設定" : room;
        ActualTime = actTime;
        Date = date;
        UserName = string.IsNullOrWhiteSpace(userName) ? "未設定" : userName; 
    }
}