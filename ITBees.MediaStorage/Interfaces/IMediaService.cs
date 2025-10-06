using ITBees.MediaStorage.Controllers;
using ITBees.MediaStorage.Controllers.Models;
using ITBees.Models.Media;
using Microsoft.AspNetCore.Http;

namespace ITBees.MediaStorage.Interfaces;

public interface IMediaService
{
    Task<UploadFileResultVm> UploadFile(IFormFile file, bool publicVisible, Guid? companyGuid);

    Task<UploadFileResultVm> UploadFile<T>(IFormFile file, bool publicVisible, Guid? companyGuid, T mediaFile)
        where T : MediaFile, new();
    Task<string> ResolveFilePath(string? resourceName, string? expectedFormat);
    Task<UploadFileResultVm> SaveFromStreamAsync(Guid? companyGuid, Stream inputStream, string originalFileName, CancellationToken ct, bool publicVisible = false);
    Task DeleteFile(string fullImageUrlPath, bool noCheckingPermissions = false);
}