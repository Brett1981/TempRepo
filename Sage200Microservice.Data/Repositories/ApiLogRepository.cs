using Sage200Microservice.Data.Models;

namespace Sage200Microservice.Data.Repositories
{
    public class ApiLogRepository : Repository<ApiLog>, IApiLogRepository
    {
        public ApiLogRepository(ApplicationContext context) : base(context)
        {
        }
    }
}