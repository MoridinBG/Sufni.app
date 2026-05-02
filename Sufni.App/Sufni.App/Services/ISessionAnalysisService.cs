using Sufni.App.Models;

namespace Sufni.App.Services;

public interface ISessionAnalysisService
{
    SessionAnalysisResult Analyze(SessionAnalysisRequest request);
}