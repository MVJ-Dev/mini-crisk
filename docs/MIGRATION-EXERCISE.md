# Ejercicio: migrar MiniCRisk de Azure a AWS

Hazlo como en un proyecto real: **primero levanta, luego diseña, después migra, valida y documenta.**
Trabaja en una rama (`aws-migration`) para que el diff sea tu evidencia. `main` queda como el estado as-is.

## 1. Criterios de éxito (versión a escala del caso real)

1. **App:** la app .NET 4.6.1, sin Cloud Services, corre en EC2 Windows Server 2022 + IIS detrás de un ALB y pasa este set de pruebas funcionales:
   - `GET /api/health` y `GET /api/health/deep` responden 200.
   - `POST /api/credit-requests` crea la solicitud y, en menos de 2 minutos, queda en estado `SCORED` (cola + R + MySQL).
   - Subir un documento y descargarlo con el link temporal (`/api/documents/{id}/link`).
   - `GET /api/reference/blacklist?name=...` lee la lista desde S3.
   - `GET /api/partner/ping` firma con el certificado (prueba el acceso a la llave privada).
2. **Datos:** MySQL migrado a RDS con DMS (full load + CDC). Integridad demostrada con `tools/validate_migration.py` → `ALL MATCH` (conteos + checksums), con las escrituras congeladas.
3. **Operación:** logs y alarmas en CloudWatch, backups de RDS, secretos en Secrets Manager con KMS, y un runbook de cutover con rollback.

## 2. Qué cambia (mapa Azure → AWS)

| Pieza (archivo) | Hoy en Azure | En AWS | Qué tocar |
|---|---|---|---|
| `Services/VaultSecretProvider.cs` | Key Vault + ADAL + certificado X.509 | Secrets Manager + rol IAM de la instancia | SDK nuevo, quitar ADAL y el certificado de autenticación, mantener caché de 60 min y nombres de secretos 1:1 |
| `Services/DocumentStorageManager.cs` (Blob) | Blob + SAS | S3 + URL prefirmada | Reusar el patrón de `S3PresignedUrlStore.cs` |
| `Services/DocumentStorageManager.cs` (File Share) | Azure File Share | Objeto en S3 | Subir `compliance/blacklist.txt` a un bucket |
| `Services/JobQueueManager.cs`, `QueueWorker.cs` | Storage Queue + cola "poison" manual | SQS + DLQ | Mismo JSON de mensaje; long polling; visibility timeout mayor al tiempo de proceso; `maxReceiveCount` en vez de `DequeueCount` |
| `WebRole.cs`, `*.csdef`, `*.cscfg` | `RoleEntryPoint` y configuración en el cscfg | `Global.asax`, `Web.config`, user data / Parameter Store | Eliminar `WebRole.cs`; mover a `Global.asax` lo que haga `OnStart()`; cada elemento del csdef pasa a ser un recurso explícito |
| `Web.Staging.config`, `Web.Release.config` | Transformaciones por ambiente | Mismas transformaciones re-apuntadas | Endpoint de RDS, región AWS, sin ajustes de Key Vault |
| `Services/PartnerRequestSigner.cs` | Certificado instalado por el cscfg | PFX importado al almacén de la instancia | Guardar el PFX en Secrets Manager e importarlo en user data; dar acceso a la llave privada a la identidad del app pool |
| `r-scoring/`, `startup.ps1` | R instalado por una startup task | AMI propia o user data | Decidir: misma EC2 o servicio aparte |
| `Functions/NightlyReport.cs` | Timer trigger | EventBridge Scheduler → Lambda | Secretos desde Secrets Manager, salida a S3 |
| `Functions/DocumentIngest.cs` | Queue trigger | SQS como event source de Lambda | Batch size, respuestas parciales de batch, DLQ |
| `db/` | MySQL (origen simulado en EC2) | RDS MySQL con DMS | Ver trampas |

## 3. Trampas sembradas (encuéntralas antes de que te encuentren)

| Trampa | Dónde | Qué debes decidir o comprobar |
|---|---|---|
| Tabla sin clave primaria | `audit_log` | Riesgo en CDC con UPDATE/DELETE. Investiga qué hace DMS y decide: agregar PK o tratarla aparte |
| Columnas LOB | `scoring_results.raw_payload` (MEDIUMTEXT, ~1.2 KB por fila en el seed) | Modo LOB limitado vs completo, tamaño máximo, impacto en velocidad |
| Claves foráneas | `credit_requests`, `documents`, `scoring_results` | Orden de carga y manejo de FKs en el full load |
| Vista, trigger y procedimiento | `v_open_requests`, `trg_credit_requests_status_audit`, `sp_close_stale_requests` | DMS migra datos, no rutinas: recréalos en el destino. El trigger va después del full load. Revisa `DEFINER` si usas un dump |
| Esquema en el destino | RDS | Según la documentación de DMS, crea tablas y claves primarias pero no todos los índices secundarios: pre-crea el esquema o verifica los índices al final |
| Binlog en el origen | MySQL en EC2 | CDC exige `log_bin`, `binlog_format=ROW`, `binlog_row_image=FULL`, retención suficiente y un usuario con permisos de replicación |
| Llave privada del certificado | `PartnerRequestSigner`, `VaultSecretProvider` | `CryptographicException` si el app pool no puede leerla |
| Mensajes duplicados | `QueueWorker` | SQS entrega al menos una vez: provoca un duplicado y comprueba que `scoring_results` no se duplica |
| Mensajes envenenados | `JobQueueManager` | Envía un mensaje inválido y comprueba que termina en la DLQ |
| IP de salida | Llamadas a terceros | Comprueba desde la instancia (`curl https://checkip.amazonaws.com`) que sale por la Elastic IP del NAT |
| Salud del ALB | `/api/health` vs `/api/health/deep` | Usa el superficial para el health check; piensa qué pasa si usas el profundo |
| Servicios en Linux vs Windows | Todo el .NET 4.6.1 | La app requiere Windows; verifica el runtime real que trae Windows Server 2022 |

## 4. Orden sugerido

1. **Levantamiento:** completa la hoja de 9 puntos con lo que ves en este repo (qué consumidores de cola hay, qué certificados, qué secretos, qué hace `OnStart()`).
2. **Red base:** VPC, subred pública y privada, NAT + Elastic IP, grupos de seguridad.
3. **Datos:** origen MySQL en EC2 con el seed; RDS destino; DMS con full load + CDC; `cdc_traffic.py` corriendo; validación.
4. **Core:** refactor en la rama `aws-migration` y despliegue en EC2 + IIS + ALB.
5. **Functions:** Lambda con SQS y EventBridge Scheduler.
6. **Cutover simulado:** congelar escrituras, esperar latencia CDC 0, validar, cambiar la conexión en el secreto, probar y ensayar el rollback.

## 5. Evidencia que debes dejar (tu bitácora)

- Tiempos: duración del full load, latencia máxima de CDC, tiempo total del cutover.
- Salida de `validate_migration.py` antes y después del cutover.
- Lista de problemas encontrados, causa raíz y solución (incluye los del primer build en Windows).
- El runbook de cutover y rollback, escrito antes de ejecutarlo.
