using Dapper;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using Newtonsoft.Json;
using perfomance_cache.Model;
using StackExchange.Redis;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace perfomance_cache.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HomeController : ControllerBase
    {

        private const string key = "get-users";
        private const string redisConn = "localhost:6379";
        private const string dbConn= "Server=localhost;database=fiap;User=root;Password=123";

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            //Implementar o cache
            var redis = ConnectionMultiplexer.Connect(redisConn);
            IDatabase db = redis.GetDatabase();
            await db.KeyExpireAsync(key, TimeSpan.FromMinutes(20)); // colocando 20 segundos no cache
            string userValue = await db.StringGetAsync(key); // verificar se há chache

            if (!string.IsNullOrEmpty(userValue))
            {
                return Ok(userValue);
            }
            
            using var connection = new MySqlConnection(dbConn);
            await connection.OpenAsync();

            string sql = "select id, name, email from users; ";
            var users = await connection.QueryAsync<Users>(sql);
            var userJson = JsonConvert.SerializeObject(users);
            await db.StringSetAsync(key, userJson); // salvando no cache
            Thread.Sleep(3000); //forçando uma espera
            return Ok(users);
        }

        [HttpPost]

        public async Task<IActionResult> Post([FromBody] Users user)
        {
            if (user == null)
                return BadRequest("Dados inválidos");

            using var connection = new MySqlConnection(dbConn);
            await connection.OpenAsync();

            string sql = @"
                insert into users(name, email)
                values(@Name ,@Email);
                select last_insert_id();
            ";

            var newId = await connection.QuerySingleAsync<int>(sql, user);
            user.Id = newId;

            //invalidar o cache
            await InvalidateCache();

            return CreatedAtAction(nameof(Get), new {id = newId}, user);
           
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Put(int id, [FromBody] Users user)
        {
            if (user == null)
                return BadRequest("Dados inválidos");

            using var connection = new MySqlConnection(dbConn);
            await connection.OpenAsync();

            user.Id = id;

            string sql = @"
                update users set
                name = @Name,
                email = @Email
                where id = @id;
            ";

            var rowsAffected = await connection.ExecuteAsync(sql, user);

            if (rowsAffected == 0)
            {
                return NotFound("Nenhum usuário encontrado!");
            }

            await InvalidateCache();
            return NoContent();

        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            if (id == 0)
            {
                return BadRequest("Identificador não informado");
            }

            using var connection = new MySqlConnection(dbConn);
            await connection.OpenAsync();

            string sql = "DELETE FROM users WHERE id = @Id";

            var rowsAffected = await connection.ExecuteAsync(sql, new {id});
            await InvalidateCache();

            return NoContent();


        }
        

        private async Task InvalidateCache()
        {
            var redis = ConnectionMultiplexer.Connect(redisConn);
            IDatabase db = redis.GetDatabase();
            await db.KeyDeleteAsync(key);
        }
    }
}
