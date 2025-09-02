using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Data.SqlClient;
using System.Text.Json;
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

        [Function("GetPaymentStatus")]
        public async Task<IActionResult> GetPaymentStatus(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "students/{studentId}/fees")] HttpRequest req,
            string studentId)
        {
            _logger.LogInformation($"C# HTTP trigger function processed a request for studentId: {studentId}.");

            if (!int.TryParse(studentId, out int id))
            {
                return new BadRequestObjectResult("Please provide a valid integer studentId.");
            }

            var connectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
            string status = "Not Found";
            decimal totalFee = 0;
            decimal paidAmount = 0;
            DateTime? dueDate = null;

            try
            {
                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();
                    var query = "SELECT TotalFee, PaidAmount, DueDate FROM Students WHERE StudentID = @StudentID";
                    using (var cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@StudentID", id);
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                totalFee = reader.GetDecimal(0);
                                paidAmount = reader.GetDecimal(1);
                                dueDate = reader.GetDateTime(2);

                                if (paidAmount >= totalFee)
                                {
                                    status = "Paid";
                                }
                                else if (paidAmount > 0)
                                {
                                    status = "Partially Paid";
                                }
                                else if (dueDate.Value < DateTime.UtcNow)
                                {
                                    status = "Overdue";
                                }
                                else
                                {
                                    status = "Pending";
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching student fee status.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            if (status == "Not Found")
            {
                return new NotFoundResult();
            }

            var result = new { studentId = id, totalFee, paidAmount, dueDate, status };
            return new OkObjectResult(result);
        }

        [Function("UpdateFeeRecord")]
        public async Task<IActionResult> UpdateFeeRecord(
            [HttpTrigger(AuthorizationLevel.Admin, "put", Route = "admin/students/{studentId}/fees")] HttpRequest req,
            string studentId)
        {
            _logger.LogInformation($"C# HTTP trigger function processed a request to update studentId: {studentId}.");

            if (!int.TryParse(studentId, out int id))
            {
                return new BadRequestObjectResult("Please provide a valid integer studentId.");
            }

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var updateData = JsonSerializer.Deserialize<FeeUpdateModel>(requestBody);

            if (updateData == null || !updateData.PaidAmount.HasValue)
            {
                return new BadRequestObjectResult("Please provide 'paidAmount' in the request body.");
            }

            var connectionString = Environment.GetEnvironmentVariable("SqlConnectionString");

            try
            {
                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();
                    var query = "UPDATE Students SET PaidAmount = @PaidAmount WHERE StudentID = @StudentID";
                    using (var cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@PaidAmount", updateData.PaidAmount.Value);
                        cmd.Parameters.AddWithValue("@StudentID", id);
                        int rowsAffected = await cmd.ExecuteNonQueryAsync();
                        if (rowsAffected == 0)
                        {
                            return new NotFoundResult();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating student fee record.");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }

            return new OkObjectResult(new { message = $"Successfully updated fee record for studentId {id}." });
        }
    }

    public class FeeUpdateModel
    {
        public decimal? PaidAmount { get; set; }
    }
}

