using Microsoft.EntityFrameworkCore;
using AppleEsportsErp.Application.DTOs;
using AppleEsportsErp.Application.Interfaces;
using AppleEsportsErp.Domain.Entities;

namespace AppleEsportsErp.Infrastructure.Services;

public class VersionService : IVersionService
{
    private readonly IUnitOfWork _unitOfWork;

    public VersionService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<VersionInfoDto?> GetLatestVersionAsync()
    {
        var version = await _unitOfWork.Repository<VersionInfo>()
            .Query()
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync();

        if (version == null)
            return null;

        return MapToVersionInfoDto(version);
    }

    public async Task<List<BranchVersionStatusDto>> GetAllBranchVersionStatusesAsync()
    {
        var statuses = await _unitOfWork.Repository<BranchVersionStatus>()
            .Query()
            .Include(s => s.Branch)
            .ToListAsync();

        var latestVersion = await GetLatestVersionAsync();
        var result = new List<BranchVersionStatusDto>();

        foreach (var status in statuses)
        {
            var dto = MapToBranchVersionStatusDto(status);
            if (latestVersion != null)
            {
                dto.LatestApprovedVersion = latestVersion.ApprovedForRollout ? latestVersion.CurrentVersion : null;
                dto.UpdateAvailable = latestVersion.ApprovedForRollout && !status.CurrentVersion.Equals(latestVersion.CurrentVersion);
            }
            result.Add(dto);
        }

        return result;
    }

    public async Task<BranchVersionStatusDto?> GetBranchVersionStatusAsync(Guid branchId)
    {
        var status = await _unitOfWork.Repository<BranchVersionStatus>()
            .Query()
            .Where(s => s.BranchId == branchId)
            .FirstOrDefaultAsync();

        if (status == null)
            return null;

        var dto = MapToBranchVersionStatusDto(status);
        var latestVersion = await GetLatestVersionAsync();

        if (latestVersion != null)
        {
            dto.LatestApprovedVersion = latestVersion.ApprovedForRollout ? latestVersion.CurrentVersion : null;
            dto.UpdateAvailable = latestVersion.ApprovedForRollout && !status.CurrentVersion.Equals(latestVersion.CurrentVersion);
        }

        return dto;
    }

    public async Task<VersionInfoDto> CreateVersionAsync(string version, string releaseNotes)
    {
        var versionInfo = new VersionInfo
        {
            CurrentVersion = version,
            ReleaseNotes = releaseNotes,
            ApprovedForRollout = false,
            CreatedAt = DateTime.UtcNow,
            BranchesApprovedCount = 0
        };

        await _unitOfWork.Repository<VersionInfo>().AddAsync(versionInfo);
        await _unitOfWork.CommitTransactionAsync();

        return MapToVersionInfoDto(versionInfo);
    }

    public async Task<VersionInfoDto> ApproveVersionAsync(int versionInfoId, string userId)
    {
        var version = await _unitOfWork.Repository<VersionInfo>()
            .Query()
            .FirstOrDefaultAsync(v => v.Id == versionInfoId);

        if (version == null)
            throw new Exception($"Version {versionInfoId} not found");

        version.ApprovedForRollout = true;
        version.ApprovedAt = DateTime.UtcNow;
        version.ApprovedByUserId = userId;

        _unitOfWork.Repository<VersionInfo>().Update(version);
        await _unitOfWork.CommitTransactionAsync();

        return MapToVersionInfoDto(version);
    }

    public async Task UpdateBranchAutoUpdateAsync(Guid branchId, bool autoUpdateEnabled)
    {
        var status = await _unitOfWork.Repository<BranchVersionStatus>()
            .Query()
            .FirstOrDefaultAsync(s => s.BranchId == branchId);

        if (status == null)
        {
            status = new BranchVersionStatus
            {
                BranchId = branchId,
                CurrentVersion = "1.0.0",
                AutoUpdateEnabled = autoUpdateEnabled,
                LastCheckedForUpdates = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow,
                GamingPcsUpToDateCount = 0,
                GamingPcsTotalCount = 0
            };
            await _unitOfWork.Repository<BranchVersionStatus>().AddAsync(status);
        }
        else
        {
            status.AutoUpdateEnabled = autoUpdateEnabled;
            status.LastCheckedForUpdates = DateTime.UtcNow;
            _unitOfWork.Repository<BranchVersionStatus>().Update(status);
        }

        await _unitOfWork.CommitTransactionAsync();
    }

    public async Task UpdateBranchVersionStatusAsync(Guid branchId, string currentVersion, int upToDateCount, int totalCount)
    {
        var status = await _unitOfWork.Repository<BranchVersionStatus>()
            .Query()
            .FirstOrDefaultAsync(s => s.BranchId == branchId);

        if (status == null)
        {
            status = new BranchVersionStatus
            {
                BranchId = branchId,
                CurrentVersion = currentVersion,
                AutoUpdateEnabled = false,
                LastCheckedForUpdates = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow,
                GamingPcsUpToDateCount = upToDateCount,
                GamingPcsTotalCount = totalCount
            };
            await _unitOfWork.Repository<BranchVersionStatus>().AddAsync(status);
        }
        else
        {
            status.CurrentVersion = currentVersion;
            status.LastCheckedForUpdates = DateTime.UtcNow;
            status.LastUpdated = DateTime.UtcNow;
            status.GamingPcsUpToDateCount = upToDateCount;
            status.GamingPcsTotalCount = totalCount;
            _unitOfWork.Repository<BranchVersionStatus>().Update(status);
        }

        await _unitOfWork.CommitTransactionAsync();
    }

    private VersionInfoDto MapToVersionInfoDto(VersionInfo version)
    {
        return new VersionInfoDto
        {
            Id = version.Id,
            CurrentVersion = version.CurrentVersion,
            ReleaseNotes = version.ReleaseNotes,
            ApprovedForRollout = version.ApprovedForRollout,
            CreatedAt = version.CreatedAt,
            ApprovedAt = version.ApprovedAt,
            ApprovedByUserId = version.ApprovedByUserId,
            BranchesApprovedCount = version.BranchesApprovedCount
        };
    }

    private BranchVersionStatusDto MapToBranchVersionStatusDto(BranchVersionStatus status)
    {
        return new BranchVersionStatusDto
        {
            Id = status.Id,
            BranchId = status.BranchId,
            BranchName = status.Branch?.Name ?? $"Branch {status.BranchId}",
            CurrentVersion = status.CurrentVersion,
            LatestApprovedVersion = status.LatestApprovedVersion,
            AutoUpdateEnabled = status.AutoUpdateEnabled,
            LastCheckedForUpdates = status.LastCheckedForUpdates,
            LastUpdated = status.LastUpdated,
            GamingPcsUpToDateCount = status.GamingPcsUpToDateCount,
            GamingPcsTotalCount = status.GamingPcsTotalCount
        };
    }
}
