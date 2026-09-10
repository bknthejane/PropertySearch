using Microsoft.EntityFrameworkCore;

namespace PropertySearch.Web.Data
{
    public class PropertyDbContext : DbContext
    {
        public PropertyDbContext(DbContextOptions<PropertyDbContext> options) : base (options)
        {

        }
    }
}
