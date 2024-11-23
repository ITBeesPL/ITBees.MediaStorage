namespace ITBees.MediaStorage.Controllers.Models;

public class UploadFileResultVm
{
    public bool PublicVisible { get; set; }

    public bool Success { get; set; }

    public long Size { get; set; }

    public string FileUrl { get; set; }
}