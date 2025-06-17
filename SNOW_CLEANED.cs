using Azure.Core;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using vCMELeaverAutomation.Model;

namespace vCMELeaverAutomation.Services
{
    public class SNOW : Authentication
    {
        private IAuthentication _authentication;
        private string _url { get; }
        private string[] _scopes { get; }
        private readonly ILogger _logger;
        public bool IsMocked { get; private set; } = bool.TryParse(Environment.GetEnvironmentVariable("SNOW:Mocked"), out var mocked) && mocked;
        public bool IsPretendSuccess { get; private set; } = bool.TryParse(Environment.GetEnvironmentVariable("SNOW:PretendSuccess"), out var pretend) && pretend;

        public SNOW(IAuthentication authentication, ILogger logger)
        {
            _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
            _url = Environment.GetEnvironmentVariable("SNOW:BaseUrl") ?? throw new ArgumentNullException("SNOW BaseUrl");
            _scopes = Environment.GetEnvironmentVariable("SNOW:Scopes")?.Split(',') ?? throw new ArgumentNullException("SNOW Scopes");
            _logger = logger;
        }

        public async Task<RitmWithTaskResponse> GetRITMByInProgressRecord(InProgressRecord inProgressRecord)
        {
            _logger.LogInformation(LogEvents.SnowService, $"Get RITM by InProgress Record: {inProgressRecord.RefCode}");

            using HttpClient client = new HttpClient();
            client.BaseAddress = new Uri(_url);

            try
            {
                var accessToken = await _authentication.GetClientCredential().GetTokenAsync(new TokenRequestContext(_scopes));
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken.Token}");
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                // Build query
                var query = $"?sysparm_limit=1";
                if (!string.IsNullOrEmpty(inProgressRecord.RITM))
                {
                    query += $"&sysparm_query=number={inProgressRecord.RITM}";
                }
                else if (!string.IsNullOrEmpty(inProgressRecord.RefCode))
                {
                    var searchDateTime = inProgressRecord.CreatedOn.AddDays(-7);
                    var dateFormatted = searchDateTime.ToString("yyyy-MM-dd");
                    var timeFormatted = searchDateTime.ToString("HH:mm:ss");
                    query += $"&sysparm_query=descriptionLIKE{inProgressRecord.RefCode}^sys_created_on>javascript:gs.dateGenerate('{dateFormatted}','{timeFormatted}')";
                }
                else
                {
                    _logger.LogInformation(LogEvents.SnowService, $"Cannot generate query to Get RITM by InProgress Record for {inProgressRecord.Mail}.");
                    return new RitmWithTaskResponse { RitmResponse = null, SCTaskSysId = null };
                }

                var response = await client.GetAsync($"/api/now/table/sc_req_item{query}");
                var data = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError(LogEvents.SnowGetRITMFailed, $"Failed to get RITM. Status: {response.StatusCode}, Message: {data}");
                    return new RitmWithTaskResponse { RitmResponse = null, SCTaskSysId = null };
                }

                var ritmResponse = JsonConvert.DeserializeObject<RITMResponse>(data);

                if (ritmResponse?.result == null || ritmResponse.result.Length == 0 || string.IsNullOrEmpty(ritmResponse.result[0]?.sys_id))
                {
                    _logger.LogWarning(LogEvents.SnowGetRITMSuccess, "RITM sys_id is null or result is empty. Cannot query SC Task.");
                    return new RitmWithTaskResponse { RitmResponse = ritmResponse, SCTaskSysId = null };
                }

                var ritmSysId = ritmResponse.result[0].sys_id;

                _logger.LogInformation(LogEvents.SnowGetRITMSuccess, $"Successfully got RITM: {ritmResponse.result[0].number}");

                var scTaskResponse = await client.GetAsync($"/api/now/table/sc_task?sysparm_limit=1&sysparm_query=request_item={ritmSysId}");
                var scTaskData = await scTaskResponse.Content.ReadAsStringAsync();

                if (!scTaskResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning(LogEvents.SnowGetRITMSuccess, $"RITM found but SC Task query failed: {scTaskData}");
                    return new RitmWithTaskResponse { RitmResponse = ritmResponse, SCTaskSysId = null };
                }

                var scTaskObj = JsonConvert.DeserializeObject<SCTaskResponse>(scTaskData);
                var scTaskSysId = scTaskObj?.result?.FirstOrDefault()?.sys_id;

                _logger.LogInformation(LogEvents.SnowGetRITMSuccess, $"Fetched SC Task sys_id: {scTaskSysId}");

                return new RitmWithTaskResponse
                {
                    RitmResponse = ritmResponse,
                    SCTaskSysId = scTaskSysId
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(LogEvents.SnowServiceException, ex, ex.Message);
                throw new Exception(ex.Message.ToString());
            }
        }
    }
}