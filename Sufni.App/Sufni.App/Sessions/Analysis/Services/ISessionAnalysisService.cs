
using Sufni.App.Sessions.Models;
namespace Sufni.App.Sessions.Analysis.Services;

public interface ISessionAnalysisService
{
    SessionAnalysisResult Analyze(SessionAnalysisRequest request);
}