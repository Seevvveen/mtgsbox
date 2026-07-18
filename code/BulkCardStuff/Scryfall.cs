using System;
using System.IO;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Sandbox.Clutter;

namespace Sandbox.BulkCardStuff;


public class Scryfall
{
	private const string Api = "https://api.scryfall.com";
	public static Scryfall Client = new();

	public struct Bulk
	{
		[JsonPropertyName("object")] public string Object { get; set; }
		public string id { get; set; }
		public string type { get; set; }
		public DateTime updated_at { get; set; }
		public string name { get; set; }
		public string description { get; set; }
		public long size { get; set; }
		public string download_uri{ get; set; }
		public string content_type { get; set; }
		public string content_encoding { get; set; }
	}

	
	private const string MetaFile = "oracle-meta.txt";
	private const string DataFile = "oracle-cards.json";
	
	private DateTime? GetLocalUpdatedAt()
	{
		if (!FileSystem.Data.FileExists(MetaFile))
			return null;

		var text = FileSystem.Data.ReadAllText(MetaFile);
		if (DateTime.TryParse(text, out var dt))
			return dt;

		return null;
	}
	
	private void SaveLocalUpdatedAt(DateTime time)
	{
		FileSystem.Data.WriteAllText(MetaFile, time.ToString("O"));
	}
	
	public async Task UpdateBulk()
	{
		var headers = new Dictionary<string, string>
		{
			["Accept"] = "application/json"
		};

		var resp = await Http.RequestJsonAsync<Bulk>(
			requestUri: "https://api.scryfall.com/bulk-data/oracle-cards",
			method: "GET",
			content: null,
			headers: headers,
			cancellationToken: CancellationToken.None
		);

		var localUpdated = GetLocalUpdatedAt();

		// If we already have this version, do nothing
		if (localUpdated.HasValue && localUpdated.Value >= resp.updated_at)
			return;

		// Download new bulk data
		byte[] data = await Http.RequestBytesAsync(
			requestUri: resp.download_uri,
			method: "GET",
			content: null,
			headers: headers,
			cancellationToken: CancellationToken.None
		);

		using Stream fileStream =
			FileSystem.Data.OpenWrite(
				DataFile,
				FileMode.Create);

		fileStream.Write(data, 0, data.Length);
		fileStream.Flush();

		SaveLocalUpdatedAt(resp.updated_at);
	}
}