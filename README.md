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
        SWA[Azure Static Web App] -->|Hospeda| SPA[Angular 20 SPA]
        SPA -->|1. Google Login| Auth[Google OAuth 2.0]
        SPA -->|2. Subida de PDF / Formulario Vacante| Gateway[Azure Container Apps Ingress]
    end

    subgraph Backend & Telemetría
        Gateway -->|HTTPS Requests + JWT| API[.NET 10 Web API]
        API -->|Logs de Consumo / Errores| AppInsights[Application Insights]
    end

    subgraph Servicios de Azure
        API -->|3. Guardar CV Original| Storage[Azure Blob Storage]
        API -->|4. Extraer Texto (OCR)| DocIntel[Azure AI Document Intelligence]
        API -->|5. Validar Habilidades y Créditos| AzureSQL[Azure SQL Database]
        API -->|6. Procesar Estructura y Optimización| OpenAI[Azure OpenAI Service]
        API -->|7. Persistencia Perfil JSON| CosmosDB[Azure Cosmos DB Serverless]
    end
```

### Flujo de Datos del Sistema
1. **Autenticación Segura**: El cliente inicia sesión mediante Google OAuth 2.0. El backend verifica la autenticidad y firma un token JWT de sesión con 24 horas de vigencia.
2. **Procesamiento de Documento**: El usuario sube su CV en PDF. El backend lo almacena en Azure Blob Storage y lo envía al servicio **Azure AI Document Intelligence** para extraer el texto estructurado, preservando el flujo de lectura (columnas, tablas y listas).
3. **Cruce de Habilidades**: El sistema extrae posibles palabras clave del texto y consulta a **Azure SQL** para cruzarlas con la taxonomía maestra, clasificándolas en habilidades oficiales y tecnologías personalizadas.
4. **Almacenamiento NoSQL**: Azure OpenAI estructura el currículum en un formato JSON estandarizado (`personalInfo`, `experience`, `education`, `skills`) que se guarda en **Azure Cosmos DB** utilizando el correo del usuario como partition key.
5. **Calibración y Optimización**: El usuario ingresa una descripción de puesto. El backend descuenta un crédito de uso del usuario (límite estricto de 20 créditos máximos para proteger presupuestos de la API), recupera el perfil estructurado y genera una versión optimizada en Markdown con su correspondiente Score de compatibilidad ATS.

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

## 4. Despliegue a Producción (How-to)

La arquitectura de producción está totalmente automatizada mediante **GitHub Actions** dividida en dos pipelines paralelos:
- **Backend**: Despliegue automático a **Azure Container Apps** mediante [.github/workflows/deploy.yml](.github/workflows/deploy.yml).
- **Frontend**: Despliegue automático a **Azure Static Web Apps** mediante [.github/workflows/deploy-frontend.yml](.github/workflows/deploy-frontend.yml).

### Paso 1: Provisionar Infraestructura en Azure
Ejecuta el despliegue del script Bicep para crear todos los recursos (incluyendo la Static Web App para el frontend):
```bash
az deployment group create --resource-group rg-cvmatchai-prod --template-file infra/main.bicep --parameters sqlAdminPassword="TuPasswordSeguro123!"
```
*Toma nota del output `frontendUrl` que se mostrará en pantalla al finalizar (por ejemplo: `https://swa-cvmatchai-prod-cah42hdmorzjm.azurestaticapps.net`).*

### Paso 2: Obtener el Token de Despliegue de la Static Web App
Consigue el token de API para configurar el pipeline del frontend ejecutando:
```bash
az staticwebapp secrets list --name "swa-cvmatchai-prod-cah42hdmorzjm" --resource-group "rg-cvmatchai-prod" --query properties.apiKey --output tsv
```

### Paso 3: Configurar Secretos en tu Repositorio de GitHub
Ve a **Settings > Secrets and variables > Actions > Secrets** en tu repositorio de GitHub e introduce las siguientes credenciales:

| Secreto de GitHub | Valor del Secreto |
| :--- | :--- |
| `AZURE_STATIC_WEB_APPS_API_TOKEN` | Token obtenido en el **Paso 2**. |
| `FRONTEND_URL` | URL de tu Static Web App obtenida en el **Paso 1** (p. ej. `https://swa-...`). |
| `AZURE_SQL_CONNECTION_STRING` | Cadena de conexión a tu base de datos de producción Azure SQL. |
| `COSMOS_CONNECTION_STRING` | Cadena de conexión a Azure Cosmos DB. |
| `AZURE_STORAGE_CONNECTION_STRING` | Cadena de conexión a Azure Storage Account (Blob). |
| `AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT` | Endpoint de Azure AI Document Intelligence. |
| `AZURE_OPENAI_ENDPOINT` | Endpoint de Azure OpenAI. |
| `AZURE_OPENAI_DEPLOYMENT_NAME` | Nombre del modelo desplegado en Azure OpenAI. |
| `JWT_KEY` | Clave secreta para firmar tokens JWT. |
| `JWT_ISSUER` | Emisor del token JWT. |
| `JWT_AUDIENCE` | Audiencia del token JWT. |
| `GOOGLE_CLIENT_ID` | Client ID de Google OAuth. |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Cadena de conexión de Azure Application Insights. |
| `ACR_USERNAME` / `ACR_PASSWORD` | Credenciales de acceso a Azure Container Registry (generadas por Bicep). |
| `AZURE_CLIENT_ID` / `AZURE_TENANT_ID` / `AZURE_SUBSCRIPTION_ID` | Credenciales de Azure para autenticación OIDC de GitHub Actions. |

### Paso 4: Lanzar el Ciclo de Despliegue
Realiza un push a la rama `main`:
```bash
git add .
git commit -m "feat: setup continuous deployment for api and frontend"
git push origin main
```
Los pipelines paralelos compilarán y publicarán tu API en Azure Container Apps e inyectarán las cabeceras CORS correctas en el backend para permitir la comunicación segura desde tu nuevo dominio de Static Web Apps.

---

## 5. Referencia de Infraestructura y Telemetría (Reference)

### Catálogo de Variables de Entorno Clave
| Variable | Tipo | Propósito |
| :--- | :--- | :--- |
| `AZURE_SQL_CONNECTION_STRING` | Conexión SQL | Acceso a las tablas relacionales para loguear uso y cruzar taxonomía. |
| `COSMOS_CONNECTION_STRING` | Conexión NoSQL | Persistencia del perfil JSON estructurado en Cosmos DB. |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Conexión Telemetría | Envío de telemetría, logs y consumo de recursos. |
| `FRONTEND_URL` | Configuración | Origen permitido por las políticas del middleware de CORS en producción. |

### Telemetría de Tokens en Azure OpenAI
El backend recopila métricas personalizadas en Application Insights cada vez que se consume el modelo de IA:
- **`OpenAiStructuringTokens`**: Mide los tokens utilizados para la normalización del perfil a JSON.
- **`OpenAiOptimizationTokens`**: Mide los tokens utilizados para redactar la propuesta optimizada de CV en Markdown.

Estas métricas te permiten monitorizar el costo operativo exacto del uso de inteligencia artificial desde el panel de métricas de Application Insights.

---

## 6. Seguridad y Políticas en Producción (Explanation)

- **Cabeceras de Seguridad Inyectadas**: Todas las respuestas de la API contienen cabeceras restrictivas contra ataques comunes:
  - `X-Content-Type-Options: nosniff` (previene sniffing de tipos MIME).
  - `X-Frame-Options: DENY` (evita ataques de secuestro de clic/clickjacking).
  - `Content-Security-Policy: default-src 'none'; frame-ancestors 'none';` (bloquea la renderización en iframes de terceros).
  - `Referrer-Policy: no-referrer` (evita la fuga de URLs y credenciales).
- **CORS Restringido**: Solo el dominio declarado en la variable `FRONTEND_URL` está autorizado para consumir los recursos de la API.
- **Credit Limit Middleware**: Para evitar facturas imprevistas por uso abusivo, el middleware intercepta cada petición a la ruta de optimización. Valida la base de datos Azure SQL y responde con un código `429 Too Many Requests` si el usuario intenta procesar más de 3 currículums.

---

## 7. Kit de Lanzamiento Social (LinkedIn Post)

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
- **Frontend**: Single Page Application con Angular 20 y reactividad nativa mediante señales alojada en Azure Static Web Apps.
- **Backend**: .NET 10 Web API protegido con JWT y cabeceras estrictas de seguridad (CORS restringido, CSP y políticas anti-clickjacking) en Azure Container Apps.
- **Almacenamiento**: Azure Blob Storage y Cosmos DB Serverless.
- **Monitoreo**: Telemetría integrada con Application Insights y alertas automáticas de presupuesto mensual.

¿Listos para llevar su currículum al siguiente nivel? ¡Prueben la beta pública hoy mismo!

🔗 **Demo frontend**: [`https://blue-river-00f861b1e.7.azurestaticapps.net`](https://blue-river-00f861b1e.7.azurestaticapps.net)


#dotnet #angular #azure #cloudcomputing #openai #artificialintelligence #jobsearch #ats
***
