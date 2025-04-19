using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.Models.Telemetry;

namespace Sufni.App.Services;

public interface IDatabaseService
{
    public Task<List<Board>> GetBoardsAsync();
    public Task<List<Board>> GetChangedBoardsAsync(int since);
    public Task PutBoardAsync(Board board);
    public Task DeleteBoardAsync(string id);
    public Task<List<Linkage>> GetLinkagesAsync();
    public Task<List<Linkage>> GetChangedLinkagesAsync(int since);
    public Task<Linkage?> GetLinkageAsync(Guid id);
    public Task<Guid> PutLinkageAsync(Linkage linkage);
    public Task DeleteLinkageAsync(Guid id);
    public Task<List<CalibrationMethod>> GetCalibrationMethodsAsync();
    public Task<List<CalibrationMethod>> GetChangedCalibrationMethodsAsync(int since);
    public Task<CalibrationMethod?> GetCalibrationMethodAsync(Guid id);
    public Task<Guid> PutCalibrationMethodAsync(CalibrationMethod calibrationMethod);
    public Task DeleteCalibrationMethodAsync(Guid id);
    public Task<List<Calibration>> GetCalibrationsAsync();
    public Task<List<Calibration>> GetChangedCalibrationsAsync(int since);
    public Task<Calibration?> GetCalibrationAsync(Guid id);
    public Task<Guid> PutCalibrationAsync(Calibration calibration);
    public Task DeleteCalibrationAsync(Guid id);
    public Task<List<Setup>> GetSetupsAsync();
    public Task<List<Setup>> GetChangedSetupsAsync(int since);
    public Task<Setup?> GetSetupAsync(Guid id);
    public Task<Guid> PutSetupAsync(Setup setup);
    public Task DeleteSetupAsync(Guid id);
    public Task<List<Session>> GetSessionsAsync();
    public Task<List<Guid>> GetIncompleteSessionIdsAsync();
    public Task<List<Session>> GetChangedSessionsAsync(int since);
    public Task<TelemetryData?> GetSessionPsstAsync(Guid id);
    public Task<byte[]?> GetSessionRawPsstAsync(Guid id);
    public Task<Guid> PutSessionAsync(Session session);
    public Task PatchSessionPsstAsync(Guid id, byte[] data);
    public Task DeleteSessionAsync(Guid id);
    public Task<SessionCache?> GetSessionCacheAsync(Guid sessionId);
    public Task<Guid> PutSessionCacheAsync(SessionCache sessionCache);
    public Task<int> GetLastSyncTimeAsync();
    public Task UpdateLastSyncTimeAsync();
}