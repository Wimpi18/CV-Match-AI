# CV-Match-AI: Motor Inteligente de Optimización y Adecuación de CV para Sistemas ATS

CV-Match-AI es una solución empresarial de vanguardia diseñada para optimizar y calibrar currículums frente a los sistemas de seguimiento de candidatos (ATS, por sus siglas en inglés). Combina técnicas avanzadas de procesamiento de lenguaje natural (NLP) e inteligencia artificial generativa sobre Azure con un motor relacional de taxonomías para proporcionar una adecuación de perfiles profesional, libre de alucinaciones y altamente estructurada.

---

## 1. Esencia del Proyecto (Explanation)

### El Problema: El Filtro Ciego de los ATS
En el mercado laboral actual, más del 70% de los CVs son descartados de manera automatizada por sistemas ATS antes de ser leídos por un humano. Estos sistemas tradicionales operan bajo reglas de coincidencia de palabras clave muy estrictas. Si un CV redacta "React.js" o "ReactJS" y el perfil del puesto exige exactamente "React", o si contiene abreviaturas o sinónimos comunes, el candidato puede quedar descalificado injustamente por diferencias ortográficas menores.

### Nuestra Solución: IA Guiada por Taxonomía Real
CV-Match-AI resuelve este problema dividiendo el flujo en dos fases críticas:
1. **Normalización por Catálogo (Fase Relacional)**: Cruzamos las habilidades extraídas del currículum con una base de datos relacional en SQL Server que actúa como catálogo taxonómico de sinónimos técnicos y habilidades canónicas. Esto unifica términos variantes (por ejemplo, normaliza "C#", "csharp", "C-Sharp" a su forma canónica "C#").
2. **Reescritura Estructurada (Fase Generativa)**: Con las habilidades previamente normalizadas y la vacante de destino, invocamos a Azure OpenAI (`gpt-4o`) utilizando un esquema de respuesta estricto (JSON Mode/Structured Outputs). La IA no inventa ni alucina experiencia; en su lugar, reorganiza y optimiza la redacción del perfil base del candidato utilizando el formato enriquecido Markdown, destacando los sinónimos y habilidades clave que el sistema ATS busca para el puesto específico.

---

## 2. Arquitectura de Referencia (Explanation)

```mermaid
graph TD
    subgraph Cliente (Frontend)
        SPA[Angular 20 SPA] -->|1. Google Login| Auth[Google OAuth 2.0]
        SPA -->|2. Subida de PDF / Formulario Vacante| Gateway[Azure Container Apps Ingress]
    end

    subgraph Backend & Telemetría
        Gateway -->|HTTPS Requests + JWT| API[.NET 10 Web API]
        API -->|Logs de Consumo / Errores| AppInsights[Application Insights]
    end

    subgraph Servicios de Azure
        API -->|3. Guardar CV Original| Storage[Azure Blob Storage]
        API -->|4. Extraer Texto (OCR)| DocIntel[Azure AI Document Intelligence]
        API -->|5. Validar Créditos y Taxonomía| AzureSQL[Azure SQL Database]
        API -->|6. Procesar Estructura y Optimización| OpenAI[Azure OpenAI Service]
        API -->|7. Persistencia Perfil JSON| CosmosDB[Azure Cosmos DB Serverless]
    end
```

### Flujo de Datos del Sistema
1. **Autenticación Segura**: El cliente inicia sesión mediante Google OAuth 2.0. El backend verifica la autenticidad y firma un token JWT de sesión con 24 horas de vigencia.
2. **Procesamiento de Documento**: El usuario sube su CV en PDF. El backend lo almacena en Azure Blob Storage y lo envía al servicio **Azure AI Document Intelligence** para extraer el texto estructurado, preservando el flujo de lectura (columnas, tablas y listas).
3. **Cruce de Habilidades**: El sistema extrae posibles palabras clave del texto y consulta a **Azure SQL** para cruzarlas con la taxonomía maestra, clasificándolas en habilidades oficiales y tecnologías personalizadas.
4. **Almacenamiento NoSQL**: Azure OpenAI estructura el currículum en un formato JSON estandarizado (`personalInfo`, `experience`, `education`, `skills`) que se guarda en **Azure Cosmos DB** utilizando el correo del usuario como partition key.
5. **Calibración y Optimización**: El usuario ingresa una descripción de puesto. El backend descuenta un crédito de uso del usuario (límite estricto de 3 créditos máximos para proteger presupuestos de la API), recupera el perfil estructurado y genera una versión optimizada en Markdown con su correspondiente Score de compatibilidad ATS.

---

## 3. Configuración y Ejecución Local (How-to)

### Requisitos Previos
- **.NET SDK 10** o superior.
- **Node.js** v20 o superior.
- **Docker Desktop** (opcional, para iniciar bases de datos locales).

### Paso 1: Configurar Variables de Entorno (`.env`)
Copia el archivo de plantilla `.env.example` en la raíz del proyecto, renombrándolo a `.env`, y rellena las credenciales correspondientes:
```bash
cp .env.example .env
```
Asegúrate de agregar todas tus cadenas de conexión de base de datos, endpoints de servicios cognitivos de Azure y credenciales OAuth de Google.


### Paso 2: Levantar y Migrar la Base de Datos Relacional
Si deseas inicializar las tablas de base de datos de manera local con Docker, ejecuta:
```bash
docker-compose up -d
```
El backend autogenera y actualiza el esquema relacional (`Users`, `JobPostings` y `UsageLogs`) y el almacén NoSQL de Cosmos DB en su primer arranque mediante el middleware de inicialización.

### Paso 3: Lanzar el Servidor Backend (.NET 10)
```bash
cd backend/CvMatchApi
dotnet run
```
La API escuchará peticiones en `http://localhost:5008`.

### Paso 4: Lanzar el Servidor Frontend (Angular)
```bash
cd frontend
npm install
npm run dev
```
Accede al portal web interactivo en `http://localhost:4200`.

### Paso 5: Validar Entorno y Conectividad
Puedes correr el validador de conexiones de base de datos e integración de flujos ejecutando:
```bash
dotnet run --project backend/ConnectionValidator/ConnectionValidator.csproj
```

---

## 4. Referencia de Infraestructura y Telemetría (Reference)

### Catálogo de Variables de Entorno Clave
| Variable | Tipo | Propósito |
| :--- | :--- | :--- |
| `AZURE_SQL_CONNECTION_STRING` | Conexión SQL | Acceso a las tablas relacionales para loguear uso y cruzar taxonomía. |
| `COSMOS_CONNECTION_STRING` | Conexión NoSQL | Persistencia del perfil estructurado en Cosmos DB. |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Conexión Telemetría | Envío de telemetría, errores y consumo de recursos. |
| `FRONTEND_URL` | Configuración | Origen permitido por las políticas del middleware de CORS. |

### Telemetría de Tokens en Azure OpenAI
El backend recopila métricas personalizadas en Application Insights cada vez que se consume el modelo de IA:
- **`OpenAiStructuringTokens`**: Mide los tokens utilizados para la normalización del perfil a JSON.
- **`OpenAiOptimizationTokens`**: Mide los tokens utilizados para redactar la propuesta optimizada de CV en Markdown.

Estas métricas te permiten monitorizar el costo operativo exacto del uso de inteligencia artificial desde el panel de métricas de Application Insights.

---

## 5. Seguridad y Políticas en Producción (Explanation)

- **Cabeceras de Seguridad Inyectadas**: Todas las respuestas de la API contienen cabeceras restrictivas contra ataques comunes:
  - `X-Content-Type-Options: nosniff` (previene sniffing de tipos MIME).
  - `X-Frame-Options: DENY` (evita ataques de secuestro de clic/clickjacking).
  - `Content-Security-Policy: default-src 'none'; frame-ancestors 'none';` (bloquea la renderización en iframes de terceros).
  - `Referrer-Policy: no-referrer` (evita la fuga de URLs y credenciales).
- **CORS Restringido**: Solo el dominio declarado en la variable `FRONTEND_URL` está autorizado para consumir los recursos de la API.
- **Credit Limit Middleware**: Para evitar facturas imprevistas por uso abusivo, el middleware intercepta cada petición a la ruta de optimización. Valida la base de datos Azure SQL y responde con un código `429 Too Many Requests` si el usuario intenta procesar más de 3 currículums.

---

## 6. Kit de Lanzamiento Social (LinkedIn Post)

***

### 🚀 Lanzamiento Oficial: CV-Match-AI (Beta Pública)

¡Me complace anunciar el lanzamiento de **CV-Match-AI**, una plataforma inteligente diseñada para superar los exigentes filtros automáticos ATS (Applicant Tracking Systems)!

Hoy en día, más del 70% de los CVs son descartados automáticamente por sistemas de seguimiento antes de llegar a manos humanas. CV-Match-AI resuelve este problema cruzando las habilidades de tu currículum con un catálogo taxonómico relacional centralizado antes de estructurar y adaptar tu currículum con IA.

#### 💡 Características Clave de la Beta:
- **Estructuración Avanzada**: Extrae texto limpio de tu currículum PDF con Azure AI Document Intelligence.
- **Normalización Taxonómica**: Compara tus habilidades con una base de datos centralizada de sinónimos técnicos en Azure SQL.
- **Adecuación de CV**: Redacta currículums optimizados en formato Markdown enriquecido gracias a Azure OpenAI.
- **Feedback Visual**: Visualizador dinámico con medidor de compatibilidad ATS interactivo y progresiones de color según adecuación.
- **Control Financiero**: Infraestructura serverless y control estricto de consumo por créditos de usuario para optimizar costos de computación.

#### 🛠️ Arquitectura Cloud de Alto Rendimiento:
- **Frontend**: Single Page Application con Angular 20 y reactividad nativa mediante señales.
- **Backend**: .NET 10 Web API protegido con JWT y cabeceras estrictas de seguridad (CORS restringido, CSP y políticas anti-clickjacking).
- **Almacenamiento**: Azure Blob Storage y Cosmos DB Serverless.
- **Monitoreo**: Telemetría integrada con Application Insights y alertas automáticas de presupuesto mensual.

¿Listos para llevar su currículum al siguiente nivel? ¡Prueben la beta pública hoy mismo!

🔗 **Demo frontend**: `https://wonderfulcliff-cd80baae.azurecontainerapps.io/`

#dotnet #angular #azure #cloudcomputing #openai #artificialintelligence #jobsearch #ats
***
