# Search benchmark

The search benchmark measures the response time of the [card search](../architecture/card-search.md).
The goal is a 95th percentile under 150 ms on the full card data.
[ADR 0021](../decisions/0021-card-search.md) records this goal.

## What the benchmark does

The console app `bench/Reliquary.Cards.Benchmark` starts the API in memory and sends requests to `GET /api/v1/cards/search`.
It uses the HTTP pipeline, the validation, the SQL statement and the JSON serialization, as a real client does.

1. It sends each query 5 times to warm up the caches of .NET and PostgreSQL.
2. It sends each query 50 times and records the time of each request.
3. It computes the 50th, 95th and 99th percentiles of each query and of all the requests.
4. It writes the results as a Markdown table.

The benchmark has 36 queries in 12 groups: names, colors, types, text, mana, statistics, legalities, tags, printings, Boolean operators, regular expressions and display options.

The exit code is 1 when the 95th percentile of all the requests is 150 ms or more.

## Run the benchmark

1. Download the Scryfall `default_cards` file and the tag files. Read [Card data](card-data.md).
2. Create an empty database for the benchmark.
3. Import the card data into this database:

   ```bash
   export ConnectionStrings__Users="Host=localhost;Port=15432;Database=reliquary_bench;Username=reliquary;Password=reliquary"
   Cards__Import__BulkType=default_cards just cli migrate
   Cards__Import__BulkType=default_cards just cli jobs run cards.import --force
   ```

4. Run the benchmark in the Release configuration:

   ```bash
   export RELIQUARY_BENCH_DATABASE="Host=localhost;Port=15432;Database=reliquary_bench;Username=reliquary;Password=reliquary"
   dotnet run -c Release --project bench/Reliquary.Cards.Benchmark -- --output search-benchmark.md
   ```

| Option | Default | Effect |
| --- | --- | --- |
| `--connection` | `RELIQUARY_BENCH_DATABASE` | The connection string of the database |
| `--iterations` | 50 | The number of measured requests for each query |
| `--warmup` | 5 | The number of warm-up requests for each query |
| `--output` | None | The Markdown file of the results. The console always gives the results. |
| `--threshold` | 150 | The limit of the 95th percentile, in milliseconds |

Do not use the demo dataset for the benchmark. It is too small to measure the performance of the search.

## GitHub workflow

The workflow `.github/workflows/search-benchmark.yml` runs the benchmark.
It starts when a pull request changes `src/Components/Reliquary.Cards` or `bench`, and you can start it manually.

1. It starts PostgreSQL 18.
2. It downloads `default_cards`, the tag files and the sets from the Scryfall API.
3. It imports the data and runs the benchmark.
4. It adds the results to the summary of the job.

Many jobs use the same runners of GitHub, so the times change from one run to the next.
Compare the results of two runs only when the difference is large.

## Results

The benchmark ran on 2026-10-26 with the code of the branch `feat/card-search`.
Other processes used the same computer during the run, so the results are a high estimate.

| Item | Value |
| --- | --- |
| Processor | Intel Xeon @ 2.10 GHz, 2 logical processors |
| Memory | 7.8 GiB |
| Operating system | Ubuntu 24.04.5 LTS |
| .NET | 10.0.14 |
| PostgreSQL | 18.6, default configuration: `shared_buffers` 128 MB, `work_mem` 4 MB |
| Card data | Scryfall `default_cards`: 39,000 Oracle cards, 119,207 printings |
| Requests | 5 warm-up requests and 50 measured requests for each query, 1,800 in all |

| Kind | Query | Results | p50 (ms) | p95 (ms) | p99 (ms) |
| --- | --- | ---: | ---: | ---: | ---: |
| Name | `bolt` | 41 | 7.7 | 13.9 | 19.8 |
| Name | `lightning bolt` | 2 | 4.1 | 9.0 | 13.9 |
| Name | `!"Sol Ring"` | 1 | 3.9 | 6.9 | 12.1 |
| Name | `jace` | 33 | 6.7 | 9.6 | 21.4 |
| Color | `c:rg` | 671 | 66.2 | 97.0 | 150.9 |
| Color | `id<=esper t:instant` | 2373 | 21.1 | 34.0 | 44.1 |
| Color | `c=2 is:bear` | 255 | 19.1 | 24.7 | 29.9 |
| Type | `t:creature` | 18853 | 80.4 | 119.4 | 142.6 |
| Type | `t:legendary t:goblin` | 67 | 7.8 | 16.0 | 32.9 |
| Text | `o:draw t:creature` | 1731 | 24.7 | 49.4 | 72.5 |
| Text | `o:"~ enters tapped"` | 762 | 31.9 | 53.0 | 87.1 |
| Text | `kw:flying -t:creature` | 59 | 17.2 | 34.5 | 38.8 |
| Mana | `m:2WW` | 844 | 18.4 | 43.5 | 63.1 |
| Mana | `mv>=7 -t:land` | 1411 | 10.0 | 16.7 | 27.6 |
| Mana | `devotion:{u/b}{u/b}{u/b}` | 249 | 86.5 | 156.3 | 165.6 |
| Stats | `pow>tou c:w t:creature` | 687 | 119.4 | 144.4 | 239.5 |
| Legality | `f:commander c:g t:creature` | 4316 | 79.9 | 128.0 | 183.3 |
| Legality | `banned:legacy` | 171 | 6.1 | 13.4 | 25.4 |
| Legality | `is:commander id:wubrg` | 3770 | 73.1 | 87.6 | 163.5 |
| Tags | `function:removal` | 6467 | 33.6 | 46.7 | 62.2 |
| Tags | `otag:ramp c:g mv<=3` | 569 | 14.6 | 27.0 | 32.6 |
| Tags | `art:squirrel` | 199 | 55.7 | 85.1 | 97.8 |
| Printing | `e:war` | 265 | 10.9 | 18.3 | 27.8 |
| Printing | `e:war is:booster r>=r` | 68 | 8.0 | 14.8 | 42.9 |
| Printing | `usd>=0.50 e:ema` | 86 | 9.8 | 16.4 | 29.9 |
| Printing | `a:avon t:land` | 79 | 77.0 | 113.6 | 146.3 |
| Printing | `game:arena year>=2024 c:u` | 1675 | 105.0 | 133.2 | 144.1 |
| Boolean | `t:legendary (t:goblin or t:elf) -c:r` | 131 | 10.9 | 18.1 | 35.2 |
| Boolean | `(o:draw or o:scry) mv<=2 id<=ub -t:land` | 712 | 18.3 | 29.1 | 36.0 |
| Boolean | `t:fish or t:bird or t:bat` | 610 | 11.9 | 21.8 | 24.8 |
| Regex | `t:creature o:/^{T}:/` | 870 | 38.6 | 58.4 | 90.4 |
| Regex | `name:/\bizzet\b/` | 21 | 5.3 | 11.1 | 21.8 |
| Regex | `t:instant o:/\spp/` | 722 | 28.8 | 39.0 | 46.5 |
| Display | `t:human order:cmc dir:desc` | 4724 | 34.2 | 46.4 | 98.8 |
| Display | `!"Lightning Bolt" unique:prints` | 73 | 6.4 | 14.4 | 42.9 |
| Display | `t:forest a:avon unique:art` | 22 | 10.8 | 16.6 | 20.8 |
| All | All the queries | | 19.1 | **105.9** | 131.7 |

The 95th percentile of all the requests is 105.9 ms. The limit is 150 ms, so the benchmark passes.

The slowest queries read many cards and cannot use an index for all their conditions:

- `devotion:` reads the mana cost of each permanent.
- `pow>tou` compares two values of each face.
- `game:arena year>=2024 c:u` reads many printings and then many cards.

The [performance design](../architecture/card-search.md#performance-design) gives the indexes and the rules that keep the other queries fast.
