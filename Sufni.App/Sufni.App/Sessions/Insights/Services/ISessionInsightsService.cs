
using Sufni.App.Sessions.Models;
namespace Sufni.App.Sessions.Insights.Services;

public interface ISessionInsightsService
{
    SessionInsightsResult Analyze(SessionInsightsRequest request);
}