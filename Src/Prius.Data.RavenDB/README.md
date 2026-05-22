# Prius.Data.RavenDB

Адаптер для интеграции с RavenDB как основного хранилища данных. Реализует паттерн обработки намерений (Intents).

## Обработка намерений
- **`DataIntentsProcessor`**: Главный цикл обработки, слушающий очередь интентов (`IDataIntentsProvider`) и транслирующий их в команды RavenDB. Поддерживает graceful shutdown через `CancellationToken` (при стазисе системы).
- **`RqlBuilder`**: Декларативный компилятор, транслирующий `QueryMap` в безопасный RQL.

## Инфраструктура
- **`DocumentStoreHolder`**: Потокобезопасный провайдер `IDocumentStore` с динамическим обновлением конфигураций и сертификатов.
- **`RavenPackageRepository`**: Реализация `IPackageRepository` для RavenDB. Поддерживает жизненный цикл системы:
  - `OnTransitionToStasis`: Очистка кэша манифестов.
  - `OnTransitionToActive`: Инициализация репозитория.

## Специфические функции
- **ID Materialization**: Детерминированное создание составных идентификаторов для результатов Map-Reduce.
- **Parallel Data Extraction**: Прямое извлечение метаданных Lucene и связанных документов из HTTP-потока RavenDB без использования тяжелых LINQ-оберток.
- **Attachments**: Потоковая передача вложений через `IBinaryManager`.

---
Подробная спецификация запросов приведена в файле `QueryMap.md`.
