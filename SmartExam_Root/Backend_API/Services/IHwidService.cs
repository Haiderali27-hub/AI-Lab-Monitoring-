namespace Backend_API.Services;

public interface IHwidService
{
    string HashFingerprint(string hardwareFingerprint);
}