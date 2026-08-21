# Analytics and Warehouses

Use an available analytics or warehouse source when the rationale depends on usage, volume, funnels, or data-pipeline behavior.

Identify the event, table, model, or dashboard that represents the behavior. Check schema and lineage before querying. Use bounded time windows, explicit environments, and aggregate results that avoid exposing personal data. Compare before and after the relevant decision when the data supports it.

Record query logic, units, time range, refresh lag, and source. Instrumentation proves measurement, not motivation. Missing events may mean missing telemetry rather than missing behavior.
