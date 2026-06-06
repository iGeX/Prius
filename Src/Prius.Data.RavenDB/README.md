# Prius.Data.RavenDB

Adapter for integration with RavenDB as the primary data store. Implements the Intent processing pattern.

## Intent Processing
* **`DataIntentsProcessor`**: The main processing loop that listens to the intent queue (`IDataIntentsProvider`) and translates them into RavenDB commands. It supports graceful shutdown via a `CancellationToken` during system stasis.
* **`RqlBuilder`**: A declarative compiler that translates a `QueryMap` into safe RQL.

## Infrastructure
* **`DocumentStoreHolder`**: A thread-safe provider of `IDocumentStore` featuring dynamic configuration and certificate updates.
* **`RavenPackageRepository`**: An implementation of `IPackageRepository` for RavenDB. It supports the system lifecycle:
    * `OnTransitionToStasis`: Manifest cache clearance.
    * `OnTransitionToActive`: Repository initialization.

## Specialized Features
* **ID Materialization**: Deterministic creation of composite identifiers for Map-Reduce results.
* **Parallel Data Extraction**: Direct extraction of Lucene metadata and associated documents from the RavenDB HTTP stream, bypassing heavy LINQ wrappers.
* **Attachments**: Streaming transmission of attachments via the `IBinaryManager`.

---
A detailed query specification is provided in the `QueryMap.md` file.
