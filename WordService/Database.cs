using System.Data;
using Microsoft.Data.SqlClient;

namespace WordService;

public sealed class Database
{
    private Coordinator _coordinator = new Coordinator();
    private static Database? _instance;

    public static Database GetInstance()
    {
        return _instance ??= new Database();
    }

    // key is the id of the document, the value is number of search words in the document
    public async Task<Dictionary<int, int>> GetDocumentsAsync(List<int> wordIds)
    {
        var res = new Dictionary<int, int>();

        var sql = @"SELECT docId, COUNT(wordId) AS count FROM Occurrences WHERE wordId IN " + AsString(wordIds) +
                  " GROUP BY docId ORDER BY count DESC;";

        var selectCmd = _coordinator.GetDocumentConnection().CreateCommand();
        selectCmd.CommandText = sql;

        using (var reader = await selectCmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var docId = reader.GetInt32(0);
                var count = reader.GetInt32(1);

                res.Add(docId, count);
            }
        }

        return res;
    }

    private string AsString(List<int> x)
    {
        return string.Concat("(", string.Join(',', x.Select(i => i.ToString())), ")");
    }

    public async Task<Dictionary<string, int>> GetAllWordsAsync()
    {
        Dictionary<string, int> res = new Dictionary<string, int>();

        foreach (var connection in _coordinator.GetAllWordConnections())
        {
            var selectCmd = connection.CreateCommand();
            selectCmd.CommandText = "SELECT * FROM Words";

            using (var reader = await selectCmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var id = reader.GetInt32(0);
                    var w = reader.GetString(1);

                    res.Add(w, id);
                }
            }
        }

        return res;
    }

    public async Task<List<string>> GetDocDetailsAsync(List<int> docIds)
    {
        List<string> res = new List<string>();

        var selectCmd = _coordinator.GetDocumentConnection().CreateCommand();

        selectCmd.CommandText = "SELECT * FROM Documents WHERE id IN " + AsString(docIds);

        using (var reader = await selectCmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var id = reader.GetInt32(0);
                var url = reader.GetString(1);

                res.Add(url);
            }
        }

        return res;
    }

    private async Task ExecuteAsync(string sql)
    {
        foreach (var connection in _coordinator.GetAllConnections())
        {
            await using var trans = await connection.BeginTransactionAsync();
            var cmd = connection.CreateCommand();
            cmd.Transaction = trans;
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
            await trans.CommitAsync();
        }
    }

    internal async Task InsertAllWordsAsync(Dictionary<string, int> res)
    {
        foreach (var word in res.Keys)
        {
            using (var transaction = await _coordinator.GetWordConnection(word).BeginTransactionAsync())
            {
                var command = _coordinator.GetWordConnection(word).CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO Words(id, name) VALUES(@id,@name)";

                var paramName = command.CreateParameter();
                paramName.ParameterName = "name";
                command.Parameters.Add(paramName);

                var paramId = command.CreateParameter();
                paramId.ParameterName = "id";
                command.Parameters.Add(paramId);

                // Insert all entries in the res

                foreach (var p in res)
                {
                    paramName.Value = p.Key;
                    paramId.Value = p.Value;
                    await command.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
            }
        }
    }

    internal async Task InsertAllOccAsync(int docId, ISet<int> wordIds)
    {
        using (var transaction = await _coordinator.GetOccurrenceConnection().BeginTransactionAsync())
        {
            var command = _coordinator.GetOccurrenceConnection().CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"INSERT INTO Occurrences(wordId, docId) VALUES(@wordId,@docId)";

            var paramwordId = command.CreateParameter();
            paramwordId.ParameterName = "wordId";

            command.Parameters.Add(paramwordId);

            var paramDocId = command.CreateParameter();
            paramDocId.ParameterName = "docId";
            paramDocId.Value = docId;

            command.Parameters.Add(paramDocId);

            foreach (var p in wordIds)
            {
                paramwordId.Value = p;
                await command.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
    }

    public async Task InsertDocumentAsync(int id, string url)
    {
        var insertCmd = _coordinator.GetDocumentConnection().CreateCommand();
        insertCmd.CommandText = "INSERT INTO Documents(id, url) VALUES(@id,@url)";

        var pName = new SqlParameter("url", url);
        insertCmd.Parameters.Add(pName);

        var pCount = new SqlParameter("id", id);
        insertCmd.Parameters.Add(pCount);

        await insertCmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteDatabase()
    {
        await ExecuteAsync("DROP TABLE IF EXISTS Occurrences");
        await ExecuteAsync("DROP TABLE IF EXISTS Words");
        await ExecuteAsync("DROP TABLE IF EXISTS Documents");
    }

    public async Task RecreateDatabase()
    {
        await ExecuteAsync("CREATE TABLE Documents(id INTEGER PRIMARY KEY, url VARCHAR(500))");
        await ExecuteAsync("CREATE TABLE Words(id INTEGER PRIMARY KEY, name VARCHAR(500))");
        await ExecuteAsync("CREATE TABLE Occurrences(wordId INTEGER, docId INTEGER, "
                           + "FOREIGN KEY (wordId) REFERENCES Words(id), "
                           + "FOREIGN KEY (docId) REFERENCES Documents(id))");
    }
}