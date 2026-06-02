namespace Backend_API.Services;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "SmartExam";
    public string Audience { get; set; } = "SmartExamClients";
    public string Key { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 30;
    public int RefreshTokenDays { get; set; } = 1;
    public string HwidPepper { get; set; } = string.Empty;
}