using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Data.SqlClient;
using System.Text.Json;
using System.Net;
using System;
using System.IO;
using System.Threading.Tasks;

namespace FeeApp.Isolated
{
    public class FeeFunctions
    {
        private readonly ILogger<FeeFunctions> _logger;

        public FeeFunctions(ILogger<FeeFunctions> logger)
        {
            _logger = logger;
        }

        // -------------------------
        // GET: Payment Status
        // -------------------------
        [Function("GetPaymentStatus")]
        public async Task<HttpResponseData> GetPaymentStatus(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "students/{studentid}/fees")] HttpRequestData req,
            string studentid)
        {
            var response = req.CreateResponse();

            if (!int.TryParse(studentid, out int id))
            {
                response.StatusCode = HttpStatusCode.BadRequest;
                await response.WriteStringAsync("Please provide a valid integer studentId.");
                return response;
            }

            var connectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
            string status = "Not Found";
            decimal totalFee = 0;
            decimal paidAmount = 0;
            DateTime? dueDate = null;

            try
            {
                using var conn = new SqlConnection(connectionString);
                await conn.OpenAsync();

                var query = "SELECT TotalFee, PaidAmount, DueDate FROM Students WHERE StudentID = @StudentID";
                using var cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@StudentID", id);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    totalFee = reader.IsDBNull(0) ? 0 : reader.GetDecimal(0);
                    paidAmount = reader.IsDBNull(1) ? 0 : reader.GetDecimal(1);
                    dueDate = reader.IsDBNull(2) ? null : reader.GetDateTime(2);

                    if (paidAmount >= totalFee) status = "Paid";
                    else if (paidAmount > 0) status = "Partially Paid";
                    else if (dueDate.HasValue && dueDate.Value < DateTime.UtcNow) status = "Overdue";
                    else status = "Pending";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching student fee status.");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteStringAsync("Error fetching student fee status.");
                return response;
            }

            if (status == "Not Found")
            {
                response.StatusCode = HttpStatusCode.NotFound;
                return response;
            }

            response.StatusCode = HttpStatusCode.OK;
            await response.WriteAsJsonAsync(new { studentId = id, totalFee, paidAmount, dueDate, status });
            return response;
        }

        // -------------------------
        // POST: Update Fee Record
        // -------------------------
        [Function("UpdateFeeRecord")]
        public async Task<HttpResponseData> UpdateFeeRecord(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "updatefee/{studentid}")] HttpRequestData req,
            string studentid)
        {
            var response = req.CreateResponse();

            if (!int.TryParse(studentid, out int id))
            {
                response.StatusCode = HttpStatusCode.BadRequest;
                await response.WriteStringAsync("Please provide a valid integer studentId.");
                return response;
            }

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            FeeUpdateModel? updateData;

            try
            {
                updateData = JsonSerializer.Deserialize<FeeUpdateModel>(requestBody,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                response.StatusCode = HttpStatusCode.BadRequest;
                await response.WriteStringAsync("Invalid JSON format.");
                return response;
            }

            if (updateData == null || !updateData.PaidAmount.HasValue)
            {
                response.StatusCode = HttpStatusCode.BadRequest;
                await response.WriteStringAsync("Please provide 'paidAmount' in the request body.");
                return response;
            }

            var connectionString = Environment.GetEnvironmentVariable("SqlConnectionString");

            try
            {
                using var conn = new SqlConnection(connectionString);
                await conn.OpenAsync();

                // Check if student exists safely
                var checkQuery = "SELECT COUNT(*) FROM Students WHERE StudentID = @StudentID";
                using var checkCmd = new SqlCommand(checkQuery, conn);
                checkCmd.Parameters.AddWithValue("@StudentID", id);

                var result = await checkCmd.ExecuteScalarAsync();
                int exists = (result == null || result == DBNull.Value) ? 0 : Convert.ToInt32(result);

                if (exists == 0)
                {
                    response.StatusCode = HttpStatusCode.NotFound;
                    await response.WriteStringAsync($"Student with ID {id} not found.");
                    return response;
                }

                // Perform update
                var updateQuery = "UPDATE Students SET PaidAmount = @PaidAmount WHERE StudentID = @StudentID";
                using var updateCmd = new SqlCommand(updateQuery, conn);
                updateCmd.Parameters.AddWithValue("@PaidAmount", updateData.PaidAmount.Value);
                updateCmd.Parameters.AddWithValue("@StudentID", id);
                await updateCmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating student fee record.");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteStringAsync("Error updating student fee record.");
                return response;
            }

            response.StatusCode = HttpStatusCode.OK;
            await response.WriteAsJsonAsync(new { message = $"Successfully updated fee record for studentId {id}." });
            return response;
        }
    }

    public class FeeUpdateModel
    {
        public decimal? PaidAmount { get; set; }
    }
}
