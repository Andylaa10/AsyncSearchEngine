using Microsoft.AspNetCore.Mvc;

namespace WordService.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class OccurrenceController : ControllerBase
    {
        private readonly Database _database = Database.GetInstance();

        [HttpPost]
        public async Task Post(int docId, [FromBody] ISet<int> wordIds)
        {
            await _database.InsertAllOccAsync(docId, wordIds);
        }
    }
}