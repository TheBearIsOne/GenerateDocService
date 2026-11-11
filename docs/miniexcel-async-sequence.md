# Complete workflow for `miniexcel`

1. The client sends a `POST /api/v1/documents/async` request containing JSON data, `engine=miniexcel`, the `xlsx` format, and—optionally—an XLSX template.
2. The API validates the request. It checks Redis to ensure an identical request is not already being processed. If it is a new task, the API saves it to PostgreSQL with a status of `Queued`, dispatches a message via MassTransit to RabbitMQ, and returns a `202 Accepted` response along with a `taskId`.
3. A worker consumes the message, updates the task status to `Processing`, and checks Redis to see if a document for this specific request has already been generated.
4. If a result is found in Redis, the worker uses the stored file reference and marks the task as complete. If no result exists, the worker invokes MiniExcel: it uses `SaveAsByTemplate` if an XLSX template is provided, or converts the JSON data into spreadsheet rows using `SaveAs` if no template is present.
5. The worker saves the XLSX file to MinIO, stores the result details in Redis, and updates the task status to `Completed` in PostgreSQL, along with the file path. If an error occurs, the system attempts retries; if these fail, the task is marked as `Failed`.
6. The client polls the `GET /api/v1/tasks/{taskId}` endpoint. Upon success, the client receives a link and downloads the file via `GET /api/v1/tasks/{taskId}/download`, at which point the API retrieves the file from MinIO.

> **Note:** There are two distinct checks involving Redis: the first prevents the redundant execution of identical requests, while the second allows for the reuse of an already generated document. Status polling retrieves the current state of the task from PostgreSQL. The asynchronous mode offloads the generation process to a worker; within this background process, MiniExcel performs the actual generation synchronously. # The Role of PostgreSQL in Async Tasks

PostgreSQL stores the **state of an async task**, rather than the XLSX file itself:

- The API creates a task record with a status of `Queued`.
- A worker updates the status to `Processing`, and subsequently to either `Completed` or `Failed`; it also records the result filename, file path, or any error text.
- The API reads this record when handling a `GET /api/v1/tasks/{taskId}` request to determine whether the result is available for download.

This allows both the API and the worker to access a consistent, persistent state, even when running as separate processes. The actual file is stored in MinIO. Redis is used for request deduplication and result caching, while RabbitMQ delivers task messages to the worker.

> **Note:** PostgreSQL is used when the `PostgreSql` provider is selected—for instance, in a Docker Compose setup. If the `InMemory` provider is configured, task records are not persisted in PostgreSQL.

### Diagram
[miniexcel-async-sequence](/docs/miniexcel-async-sequence.mmd)