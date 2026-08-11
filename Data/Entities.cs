using System.ComponentModel.DataAnnotations;

namespace HajjVR.Data;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Operator = "Operator";
    public const string Pembimbing = "Pembimbing";
    public const string Jamaah = "Jamaah";
    public static readonly string[] All = [Admin, Operator, Pembimbing, Jamaah];
}

public class AppUser
{
    public int Id { get; set; }
    [MaxLength(64)] public string UserName { get; set; } = "";
    [MaxLength(128)] public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    [MaxLength(128)] public string DisplayName { get; set; } = "";
    [MaxLength(32)] public string Role { get; set; } = Roles.Jamaah;
    public string? AvatarUrl { get; set; }
    [MaxLength(8)] public string Language { get; set; } = "id";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? ResetToken { get; set; }
    public DateTime? ResetTokenExpires { get; set; }

    public JamaahProfile? Profile { get; set; }
}

public class JamaahProfile
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public AppUser? User { get; set; }
    [MaxLength(64)] public string GroupName { get; set; } = "";
    public int? PembimbingUserId { get; set; }
    [MaxLength(64)] public string Nationality { get; set; } = "Indonesia";
    [MaxLength(32)] public string PassportNumber { get; set; } = "";
    public DateTime? BirthDate { get; set; }
    [MaxLength(32)] public string Phone { get; set; } = "";
    [MaxLength(16)] public string PackageType { get; set; } = "Umrah"; // Umrah | Haji
    public string? Notes { get; set; }
}

public enum RitualType
{
    Ihram = 0,
    Thawaf = 1,
    Sai = 2,
    Tahalul = 3,
    WukufArafah = 4,
    MabitMuzdalifah = 5,
    LemparJumrah = 6,
    MabitMina = 7,
    ThawafIfadah = 8,
    ThawafWada = 9,
    ZiarahNabawi = 10
}

public enum ProgressStatus { NotStarted = 0, InProgress = 1, Completed = 2 }

public class RitualProgress
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public AppUser? User { get; set; }
    public RitualType Ritual { get; set; }
    public ProgressStatus Status { get; set; } = ProgressStatus.NotStarted;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int DurationMinutes { get; set; }
    public string? Notes { get; set; }
}

public class Location
{
    public int Id { get; set; }
    [MaxLength(128)] public string Name { get; set; } = "";
    [MaxLength(128)] public string NameArabic { get; set; } = "";
    public string Description { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    [MaxLength(32)] public string Category { get; set; } = ""; // Masjid, Ritual, Ziarah, Fasilitas
    [MaxLength(32)] public string SceneKey { get; set; } = ""; // key scene 3D: haram, nabawi, manasik
    [MaxLength(32)] public string Zone { get; set; } = "";
}

public class JamaahDocument
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public AppUser? User { get; set; }
    [MaxLength(200)] public string Title { get; set; } = "";
    [MaxLength(256)] public string FileName { get; set; } = "";
    [MaxLength(512)] public string Url { get; set; } = "";
    [MaxLength(128)] public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    /// <summary>Teks isi dokumen (untuk pencarian semantik).</summary>
    public string ContentText { get; set; } = "";
    [MaxLength(32)] public string Kind { get; set; } = "Dokumen"; // Gambar | Video | Dokumen | Panduan
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}

public class ChatSession
{
    [MaxLength(40)] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int UserId { get; set; }
    [MaxLength(200)] public string Title { get; set; } = "Percakapan Baru";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<ChatMessageEntity> Messages { get; set; } = [];
}

public class ChatMessageEntity
{
    public int Id { get; set; }
    [MaxLength(40)] public string SessionId { get; set; } = "";
    public ChatSession? Session { get; set; }
    [MaxLength(16)] public string Role { get; set; } = "user"; // user | assistant
    public string Content { get; set; } = "";
    /// <summary>JSON: [{"url","name","contentType","isImage"}]</summary>
    public string? AttachmentsJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Badge
{
    public int Id { get; set; }
    [MaxLength(48)] public string Code { get; set; } = "";
    [MaxLength(128)] public string Name { get; set; } = "";
    [MaxLength(256)] public string Description { get; set; } = "";
    [MaxLength(16)] public string Icon { get; set; } = "🏅";
    public int Points { get; set; } = 10;
}

public class UserBadge
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public AppUser? User { get; set; }
    public int BadgeId { get; set; }
    public Badge? Badge { get; set; }
    public DateTime AwardedAt { get; set; } = DateTime.UtcNow;
}

public class AppSetting
{
    [MaxLength(128)] public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>Snapshot jumlah jamaah per zona untuk heatmap & analitik real-time.</summary>
public class CrowdSnapshot
{
    public int Id { get; set; }
    [MaxLength(48)] public string Zone { get; set; } = "";
    public int Count { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class ActivityLog
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    [MaxLength(64)] public string Action { get; set; } = "";
    [MaxLength(512)] public string Detail { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
