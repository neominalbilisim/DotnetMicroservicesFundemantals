# Neominal Microservices Mastery — Modül 1 Uygulama Şablonu

Bu proje, **.NET Microservices Mastery** eğitiminin 1. Modülü ("Kurumsal Mimari, Containerizasyon ve Gözlemlenebilirlik") kapsamında; self-hosted Kestrel, merkezi hata yönetimi, structured logging, distributed tracing, metrics, PostgreSQL, Redis, Kafka, Vault ve Nginx reverse proxy entegrasyonlarını **tek bir .NET 10 servisi** üzerinden uygulamalı olarak deneyimlemeniz için hazırlanmıştır.

---

## 1. Mimari Özet

```
                          ┌───────────────┐
   İstemci (curl/Postman) │     Nginx     │  :8090  (Reverse Proxy)
        ───────────────▶  │ (reverse proxy)│
                          └───────┬───────┘
                                  │
                                  ▼
                     ┌────────────────────────┐
                     │   .NET 10 Uygulaması    │  :8080
                     │ (Kestrel, self-hosted)  │
                     └───┬───────┬───────┬────┘
                         │       │       │
            ┌────────────┘   ┌───┘       └────────────┐
            ▼                ▼                        ▼
      ┌───────────┐   ┌────────────┐           ┌─────────────┐
      │ PostgreSQL │   │   Redis    │           │    Kafka     │
      └───────────┘   └────────────┘           └─────────────┘
            │                                          │
            ▼                                          ▼
      ┌───────────┐                             ┌─────────────┐
      │   Vault    │                             │   Grafana /  │
      └───────────┘                             │  Prometheus  │
                                                  └─────────────┘
                     ┌─────────────┐   ┌──────────────┐
                     │     Seq      │   │    Jaeger     │
                     │ (loglar)     │   │  (trace'ler)  │
                     └─────────────┘   └──────────────┘
```

| Katman | Teknoloji | Bu şablondaki rolü |
|---|---|---|
| Web sunucu | Kestrel (self-hosted) | IIS'siz, doğrudan HTTP karşılama |
| Reverse proxy | Nginx | Gerçek prod topolojisini simüle eder |
| Veritabanı | PostgreSQL | EF Core ile CRUD senaryosu |
| Cache | Redis | Cache-aside senaryosu |
| Mesajlaşma | Kafka (KRaft) | Publish/consume bağlantı testi |
| Mesajlaşma (alternatif) | RabbitMQ | Publish/consume bağlantı testi (manuel ack ile) |
| Background Jobs | Hangfire (PostgreSQL storage) | Fire-and-forget / scheduled / recurring job senaryoları |
| Identity & Access Management | Keycloak | Altyapı olarak sağlanır (bu şablonda .NET auth entegrasyonu henüz yapılmadı) |
| Service Discovery | HashiCorp Consul | Altyapı olarak sağlanır (bu şablonda .NET service registration henüz yapılmadı) |
| Secret yönetimi | HashiCorp Vault (dev mode) | Runtime'da secret okuma/yazma |
| Loglama | Serilog → Seq | Structured (JSON) loglama |
| Tracing | OpenTelemetry → Jaeger | Uçtan uca istek izleme |
| Metrics | OpenTelemetry → Prometheus → Grafana | Sistem/iş metrikleri |

---

## 2. Ön Koşullar

- [.NET 10 SDK](https://dotnet.microsoft.com/) kurulu olmalı (`dotnet --version` ile doğrulayın)
- Docker Desktop (Win/Mac) veya Docker Engine + Compose plugin (Linux)
- En az 8 GB boş RAM (8-9 container aynı anda çalışacak)
- `curl` veya Postman/Bruno gibi bir API test aracı

---

## 3. Adım 1 — Ortam Değişkenlerini Ayarlama ve Altyapıyı Ayağa Kaldırma

### 3.0 `.env` Dosyası

Bu şablon, Seq ve Grafana admin şifrelerini **`docker-compose.infra.yml` içine gömmek yerine** proje kökündeki `.env` dosyasından okur — gerçek bir prod ortamında secret'ların compose dosyasının içine hardcode edilmemesi gerektiği prensibini yansıtır.

Repoda hazır bir `.env` dosyası (varsayılan şifrelerle) ve bir `.env.example` şablonu bulunur. **İlk iş olarak `.env` içindeki şifreleri kendi şifrelerinizle değiştirin:**

```bash
# .env dosyasını açıp SEQ_ADMIN_PASSWORD ve GRAFANA_ADMIN_PASSWORD değerlerini güncelleyin
```

> `.env` dosyası `.gitignore`'dadır — asla versiyon kontrolüne (Git) commit etmeyin. Takım arkadaşlarınızla paylaşmanız gerekirse `.env.example`'ı temel alıp güvenli bir kanaldan (parola yöneticisi, Vault vb.) iletin.

> **Önemli:** Seq ve Grafana admin şifreleri sadece **ilgili volume boşken (ilk çalıştırmada)** set edilir. `.env` dosyasını sonradan değiştirmek, zaten oluşturulmuş bir kullanıcının şifresini otomatik güncellemez — şifreyi değiştirmek isterseniz ya ilgili servisin arayüzünden (Grafana) / CLI'sinden (Seq) değiştirmeniz, ya da `docker compose down -v` ile volume'ü sıfırlayıp yeniden ilk-çalıştırma yapmanız gerekir.

### 3.1 Altyapıyı Ayağa Kaldırma

Proje kök dizininde:

```bash
docker compose -f docker-compose.infra.yml up -d
```

İlk çalıştırmada imajların indirilmesi birkaç dakika sürebilir. Durumu kontrol edin:

```bash
docker compose -f docker-compose.infra.yml ps
```

Tüm servislerin `healthy` durumuna geçmesini bekleyin (özellikle Kafka biraz zaman alabilir).

Altyapıyı durdurmak için:

```bash
docker compose -f docker-compose.infra.yml down
```

Verileri de (Postgres, Grafana, Vault) silmek isterseniz:

```bash
docker compose -f docker-compose.infra.yml down -v
```

### 3.1 Servis Erişim Tablosu

| Servis | Adres | Bilgi |
|---|---|---|
| PostgreSQL | `localhost:15432` | db: `neominal_demo`, user: `neominal`, pass: `neominal_pass` (bkz. not) |
| Redis | `localhost:6379` | şifresiz |
| Kafka (host'tan) | `localhost:19094` | EXTERNAL listener (standart olmayan port, bkz. not) |
| Kafka (container'dan) | `kafka:9092` | PLAINTEXT listener |
| Kafka UI | http://localhost:8082 | şifre yok, cluster otomatik tanımlı (`kafka:9092`) |
| RedisInsight | http://localhost:5540 | şifre yok; bağlantıyı elle eklemeniz gerekir (bkz. 3.4) |
| Vault UI/API | http://localhost:8200 | token: `root` |
| Seq (log arayüzü) | http://localhost:8081 | user: `admin`, pass: `.env` → `SEQ_ADMIN_PASSWORD` (ingestion: `localhost:5341`) |
| Jaeger UI | http://localhost:16686 | OTLP: `localhost:4317` (gRPC) |
| Prometheus | http://localhost:9090 | |
| Grafana | http://localhost:3000 | user: `admin`, pass: `.env` → `GRAFANA_ADMIN_PASSWORD` |
| Nginx (reverse proxy) | http://localhost:8090 | uygulamaya (`:8080`) proxy'ler |
| Hangfire Dashboard | http://localhost:8080/hangfire | şifre yok (demo amaçlı açık, bkz. not) |
| Keycloak Admin Console | http://localhost:8180 | user: `admin`, pass: `admin` |
| Consul UI | http://localhost:8500/ui | şifre yok (dev mode) |
| RabbitMQ (AMQP, host'tan) | `localhost:25672` | user: `neominal`, pass: `neominal_pass` (standart olmayan port, bkz. not) |
| RabbitMQ (AMQP, container'dan) | `rabbitmq:5672` | user: `neominal`, pass: `neominal_pass` |
| RabbitMQ Management UI | http://localhost:15672 | user: `neominal`, pass: `neominal_pass` |

> **Not (Postgres portu):** Bu şablon, Postgres'i standart `5432` yerine host'ta **`15432`** portundan yayınlar (container içinde hâlâ `5432`). Bazı Windows makinelerinde antivirüs/kurumsal güvenlik yazılımları veritabanlarının "bilinen" portlarını (1433, 3306, 5432, 27017 vb.) izleyip host↔container bağlantısını sessizce kesebiliyor; bu da .NET/DBeaver gibi istemcilerde `SocketException (10053)` veya `EOFException` şeklinde görünür. `15432` gibi standart olmayan bir port bu sorunu çoğunlukla by-pass eder.
>
> **Not (Kafka portu):** Aynı sebeple Kafka'nın host'a açık portu da standart `9094` yerine **`19094`** olarak ayarlandı. Eğer host'tan Kafka'ya bağlanırken `rdkafka` loglarında `Disconnected while requesting ApiVersion` gibi bir hata görürseniz, bu da aynı kategoride bir host↔container bağlantı kesintisidir.
>
> **Not (RabbitMQ portu):** Aynı önleyici tedbir RabbitMQ'nun AMQP portu için de uygulandı — host'ta standart `5672` yerine **`25672`** kullanılır (container içinde hâlâ `5672`). Management UI (`15672`, HTTP tabanlı) standart portunda bırakıldı çünkü web arayüzleri bu tür kesintilerden genelde etkilenmiyor.
>
> **Not (Keycloak / Consul):** Bu iki servis şu an sadece **altyapı olarak** sağlanıyor — Keycloak'ta bir realm/client tanımlı değil, .NET uygulaması henüz Keycloak üzerinden JWT doğrulaması yapmıyor; Consul'a da uygulama kendini bir servis olarak kaydetmiyor. İkisi de ayakta ve erişilebilir durumda; ileride "Polyglot servisler ve API Gateway yaklaşımları" ya da kimlik doğrulama modülünde bu entegrasyonları ekleyebiliriz.

### 3.2 Vault'ta İlk Secret'ı Oluşturma

Vault dev mode'da açılır ama içi boştur. Uygulamayı test etmeden önce bir örnek secret yazın:

```bash
docker exec -it neominal-vault vault kv put secret/demo-app \
    ConnectionStrings__ExternalApi="https://example.com" \
    ApiKey="s3cr3t-demo-key"
```

Doğrulamak için:

```bash
docker exec -it neominal-vault vault kv get secret/demo-app
```

> Not: Uygulama içindeki `/demo/vault/secret/{path}` endpoint'i de bu secret'ı runtime'da okuyup/yazabilir; ayrıca bu CLI adımını atlayıp doğrudan `POST /demo/vault/secret/demo-app` ile de secret yazabilirsiniz.

### 3.3 Seq için API Key Oluşturma (Authentication Açıkken Log Gönderebilmek İçin)

Artık Seq'e gerçek bir admin şifresiyle giriş yapılıyor (bkz. Bölüm 3.0). Authentication açık olduğunda, güvenlik ayarlarına bağlı olarak uygulamanın log gönderebilmesi için bir **API Key** gerekebilir:

1. http://localhost:8081 adresine gidip `admin` / `.env` dosyanızdaki `SEQ_ADMIN_PASSWORD` ile giriş yapın.
2. Sağ üstten **Settings → API Keys → Add API Key** yolunu izleyin.
3. Bir isim verin (örn. `neominal-app`), oluşturun ve verilen token'ı kopyalayın.
4. Bu token'ı uygulamanın `appsettings.json` dosyasındaki `Seq:ApiKey` alanına yapıştırın **veya** ortam değişkeni olarak geçin:

   ```bash
   Seq__ApiKey=<kopyaladığınız-token> dotnet run
   ```

> Eğer loglar zaten Seq'te görünüyorsa (401 hatası almıyorsanız), bu adımı atlayabilirsiniz — Seq'in varsayılan ayarlarında ingestion API key zorunlu olmayabilir. Sorun yaşarsanız yukarıdaki adımı uygulayın.

### 3.4 Kafka UI ve RedisInsight'a Bağlanma

#### Kafka UI

Kafka UI, `kafka:9092` cluster'ını `docker-compose.infra.yml` üzerinden otomatik olarak tanır — ekstra bir kurulum adımı gerekmez.

1. Tarayıcıda **http://localhost:8082** adresini açın (şifre/login yok).
2. Sol menüden **Topics** sekmesine girin — `demo-events` topic'ini göreceksiniz (`/demo/kafka/publish` ile gönderdiğiniz mesajlar burada birikir).
3. **Consumers** sekmesinden `demo-consumer-group`'un lag/offset durumunu izleyebilirsiniz.
4. Bir topic'e tıklayıp **Messages** sekmesinden mesaj içeriklerini görsel olarak inceleyebilirsiniz.

#### RedisInsight

RedisInsight'ta ise bağlantıyı **ilk açılışta elle eklemeniz** gerekir:

1. Tarayıcıda **http://localhost:5540** adresini açın (şifre/login yok).
2. **"Add Redis database"** (veya "I already have a database") seçeneğine tıklayın.
3. Aşağıdaki bilgileri girin:
   - **Host:** `redis`
   - **Port:** `6379`
   - **Database Alias:** `neominal-redis` (istediğiniz bir isim)
   - Şifre alanını boş bırakın (bu şablonda Redis şifresizdir).
4. **Add Redis Database** ile kaydedin.
5. Sol menüden **Browser** sekmesine geçin — `/demo/cache/{key}` endpoint'iyle yazdığınız key-value çiftlerini burada görürsünüz.

---

## 4. Adım 2 — .NET Uygulamasını Çalıştırma

```bash
cd src/Neominal.Microservices.Template
dotnet restore
dotnet run
```

Uygulama varsayılan olarak `http://localhost:8080` üzerinden ayağa kalkar (appsettings ile Kestrel portu `ASPNETCORE_URLS` env değişkeniyle de override edilebilir; şablon `8080` portunu hem yerelde hem container'da tutarlı kullanır).

Ayağa kalktığını doğrulamak için:

```bash
curl http://localhost:8080/
curl http://localhost:8080/health
```

> **Not (Windows/Mac):** `host.docker.internal` Docker Desktop'ta otomatik çözümlenir. **Linux** kullanıyorsanız `docker-compose.infra.yml` içinde ilgili servislere zaten `extra_hosts: host.docker.internal:host-gateway` eklendi — ek bir işlem gerekmez.

---

## 5. Adım 3 — Senaryoları Test Etme

> **Postman kullanıcıları:** Aşağıdaki tüm istekleri tek tek `curl` ile çalıştırmak yerine, proje kökündeki `Neominal.Microservices.Template.postman_collection.json` dosyasını Postman'e import edebilirsiniz (**File → Import**). Koleksiyon, her endpoint için örnek body/path değerleriyle ve neyin ne işe yaradığını açıklayan notlarla birlikte gelir. Varsayılan `baseUrl` değişkeni `http://localhost:8080`'dir; nginx üzerinden test etmek isterseniz koleksiyonun **Variables** sekmesinden `baseUrl`'i `http://localhost:8090` olarak değiştirin.

### 5.1 PostgreSQL — Kayıt Ekleme/Okuma

```bash
curl -X POST http://localhost:8080/demo/db/products \
  -H "Content-Type: application/json" \
  -d '{"name":"Mekanik Klavye","price":149.90}'

curl http://localhost:8080/demo/db/products
```

Geçersiz veri gönderip validasyonu tetikleyin:

```bash
curl -X POST http://localhost:8080/demo/db/products \
  -H "Content-Type: application/json" \
  -d '{"name":"","price":-5}'
```

### 5.2 Global Exception Handling

```bash
curl -i http://localhost:8080/demo/errors/not-found
curl -i http://localhost:8080/demo/errors/unauthorized
curl -i http://localhost:8080/demo/errors/bad-request
curl -i http://localhost:8080/demo/errors/unknown
```

Her biri RFC 7807 (`application/problem+json`) formatında, uygun HTTP status code ile döner. Seq'te (`http://localhost:8081`) bu hataların loglandığını görebilirsiniz.

### 5.3 Redis Cache-Aside

```bash
curl http://localhost:8080/demo/cache/kullanici-42
curl http://localhost:8080/demo/cache/kullanici-42   # ikinci çağrıda "source": "redis-cache" döner
```

### 5.4 Kafka Publish / Consume

> **Otomatik topic oluşturma:** Uygulama açılışında `demo-events` topic'ini **kendisi, doğru ayarlarla** (5 partition, replication factor=1 — tek broker'lı cluster'ımıza uygun) otomatik olarak oluşturur; Kafka UI'da elle topic oluşturmanıza gerek yoktur. Eğer daha önce elle **farklı bir replication factor ile** bir `demo-events` topic'i oluşturduysanız, uygulama onu "zaten var" kabul edip dokunmaz — bu durumda Kafka UI'dan o topic'i silip uygulamayı yeniden başlatın ki doğru ayarlarla yeniden oluşturulsun.

```bash
curl -X POST http://localhost:8080/demo/kafka/publish \
  -H "Content-Type: application/json" \
  -d '{"message":"Merhaba Kafka!"}'

sleep 2
curl http://localhost:8080/demo/kafka/messages
```

### 5.5 RabbitMQ Publish / Consume

```bash
curl -X POST http://localhost:8080/demo/rabbitmq/publish \
  -H "Content-Type: application/json" \
  -d '{"message":"Merhaba RabbitMQ!"}'

sleep 1
curl http://localhost:8080/demo/rabbitmq/messages
```

Mesajın kuyruğa girdiğini ve tüketildiğini RabbitMQ Management UI'da (`http://localhost:15672`) **Queues → demo-queue** sekmesinden de canlı olarak izleyebilirsiniz (Ready/Unacked/Total sayaçları).

### 5.6 Vault Secret Okuma/Yazma

```bash
curl http://localhost:8080/demo/vault/secret/demo-app

curl -X POST http://localhost:8080/demo/vault/secret/demo-app \
  -H "Content-Type: application/json" \
  -d '{"NewKey":"NewValue"}'
```

### 5.7 Hangfire — Background Jobs

> **Not:** Hangfire ayrı bir Docker servisi değildir — .NET uygulamasının içine gömülü çalışan bir kütüphanedir. Job kayıtlarını (queue, schedule, recurring) mevcut PostgreSQL veritabanımızdaki `hangfire` şemasında saklar; bu şema uygulama ilk açıldığında Hangfire tarafından otomatik oluşturulur (ek bir migration adımı gerekmez).

**Fire-and-forget (anında kuyruğa alınır):**
```bash
curl -X POST http://localhost:8080/demo/jobs/fire-and-forget \
  -H "Content-Type: application/json" \
  -d '{"message":"Merhaba Hangfire!"}'
```

**Scheduled (gecikmeli, örn. 10 saniye sonra):**
```bash
curl -X POST http://localhost:8080/demo/jobs/schedule \
  -H "Content-Type: application/json" \
  -d '{"message":"10 saniye sonra calisacak job","delaySeconds":10}'
```

**Recurring (cron ifadesiyle tekrarlayan, örnekte her dakika):**
```bash
curl -X POST http://localhost:8080/demo/jobs/recurring \
  -H "Content-Type: application/json" \
  -d '{"message":"Her dakika calisan job","cronExpression":"*/1 * * * *"}'

# Kaldirmak icin:
curl -X DELETE http://localhost:8080/demo/jobs/recurring
```

Tüm bu job'ların çalıştığını **http://localhost:8080/hangfire** adresindeki Dashboard'dan canlı olarak izleyebilirsiniz (Succeeded/Processing/Scheduled/Recurring sekmeleri). Job içindeki log satırlarını Seq'te de görebilirsiniz (`[Hangfire Job] ...` ile başlar).

> ⚠️ **Güvenlik notu:** Dashboard bu şablonda kimlik doğrulaması olmadan açıktır (sadece eğitim/dev kolaylığı için). Gerçek bir prod ortamında `UseHangfireDashboard` çağrısına mutlaka bir `IDashboardAuthorizationFilter` eklenmelidir.

### 5.8 Observability Doğrulaması

- **Loglar:** http://localhost:8081 (Seq) → uygulama adına göre filtreleyin (`Application = neominal-microservices-template`)
- **Trace'ler:** http://localhost:16686 (Jaeger) → Service: `neominal-microservices-template` seçip "Find Traces"
- **Metrikler:** http://localhost:9090 (Prometheus) → `http_server_request_duration_seconds_count` gibi bir metrik sorgulayın
- **Dashboard:** http://localhost:3000 (Grafana) → Data source olarak Prometheus'u (`http://prometheus:9090`) ekleyip yukarıdaki metrikle basit bir panel oluşturun

---

## 6. Nginx Reverse Proxy Üzerinden Test

Nginx, `.env`/compose ayarına göre host'un **8090** portunda dinler ve gelen isteği `.NET` uygulamasının **8080** portuna (host.docker.internal:8080) iletir. Yani:

- **`http://localhost:8080`** → doğrudan uygulama (nginx'i bypass eder)
- **`http://localhost:8090`** → nginx üzerinden uygulama (gerçek prod akışını simüle eder)

Uygulamayı her zamanki gibi çalıştırın (artık `launchSettings.json` sayesinde varsayılan olarak `8080`'de açılır, ek bir env değişkeni gerekmez):

```bash
cd src/Neominal.Microservices.Template
dotnet run
```

Şimdi iki adresi karşılaştırın:

```bash
# Doğrudan uygulama
curl -i http://localhost:8080/

# Nginx üzerinden (aynı cevabı almalısınız, ama araya nginx girmiş durumda)
curl -i http://localhost:8090/
curl -i http://localhost:8090/demo/db/products
```

`X-Forwarded-For`, `X-Forwarded-Proto` gibi header'ların uygulamaya doğru iletildiğini görmek için `http://localhost:8090/demo/errors/bad-request` gibi bir endpoint'e istek atıp Seq loglarını inceleyebilirsiniz — nginx'in eklediği header'ları uygulama tarafında görürsünüz.

> **Neden iki farklı port?** Gerçek bir prod ortamında uygulamanın portu dışarıya hiç açılmaz — sadece nginx'in portu (genelde 80/443) dışarıya açıktır. Bu şablonda öğrenme/hata ayıklama kolaylığı için uygulamanın portunu da (`8080`) host'a açık bıraktık; isterseniz `docker-compose.infra.yml`'de uygulamayı container'a aldığınızda (Bölüm 7) bu ihtiyaç ortadan kalkar çünkü uygulama artık host'a değil, sadece Docker network'üne açık olur.

Tam prod-like bir kurulum (uygulamayı da container'a alma) için: Bkz. Bölüm 7.

---

## 7. (Opsiyonel) Uygulamayı da Container'a Alarak Tam Prod Topolojisi Kurma

Şu ana kadar .NET uygulaması host makinede `dotnet run` ile çalıştı. Tam bir production simülasyonu için uygulamayı da aynı Docker network'üne container olarak ekleyebilirsiniz:

**7.1 — İmajı build edin:**

```bash
cd src/Neominal.Microservices.Template
docker build -t neominal-microservices-template:latest .
```

**7.2 — `docker-compose.infra.yml` dosyasına aşağıdaki servisi ekleyin** (mevcut `services:` bloğunun altına):

```yaml
  app:
    image: neominal-microservices-template:latest
    container_name: neominal-app
    restart: unless-stopped
    environment:
      ASPNETCORE_ENVIRONMENT: Container
    depends_on:
      - postgres
      - redis
      - kafka
      - vault
      - seq
      - jaeger
    networks:
      - neominal-net
```

**7.3 — `appsettings.Container.json` zaten servis-adı bazlı bağlantılarla hazır** (postgres, redis, kafka, vault, seq, jaeger container isimleri kullanılır). `ASPNETCORE_ENVIRONMENT=Container` set edildiğinde .NET bu dosyayı otomatik olarak `appsettings.json` üzerine katmanlar.

**7.4 — `prometheus/prometheus.yml` ve `nginx/nginx.conf` içindeki hedefleri güncelleyin:**

```yaml
# prometheus.yml
static_configs:
  - targets: ["app:8080"]
```

```nginx
# nginx.conf
upstream dotnet_app {
    server app:8080;
}
```

**7.5 — Her şeyi tek seferde ayağa kaldırın:**

```bash
docker compose -f docker-compose.infra.yml up -d --build
```

Artık `http://localhost:8090` (Nginx) → `app` container → tüm altyapı, tamamen container'lar arası network üzerinden çalışır; host portu bağımlılığı kalmaz (uygulamanın `8080` portunu host'a hiç açmayabilirsiniz).

---

## 8. Production-Ready Notlar

Bu şablon eğitim amaçlıdır ancak gerçek bir prod ortamına taşırken dikkat edilmesi gereken noktalar:

| Konu | Şablondaki durum | Prod'da yapılması gereken |
|---|---|---|
| Veritabanı şeması | `EnsureCreated()` | `dotnet ef migrations` ile versiyonlanmış migration'lar |
| Vault modu | Dev mode (in-memory, tek node) | Production mode: storage backend (Consul/Raft), unseal süreci, gerçek auth method (AppRole, Kubernetes vb.) |
| Seq authentication | Gerçek admin şifresi (`.env` → `SEQ_ADMIN_PASSWORD`) | Şifreyi bir secret store'dan (Vault, Key Vault) çekin; `.env` dosyasını asla repoya commit etmeyin; ingestion için API key kullanın |
| Keycloak | Dev mode, gömülü DB, admin/admin | Prod'da `start` komutu (dev değil), harici Postgres, HTTPS zorunlu, admin şifresi Vault'tan |
| Consul | Dev mode, tek node, ACL kapalı | Prod'da çok node'lu cluster, ACL/TLS açık, gossip encryption |
| RabbitMQ | Tek node, düz kullanıcı/şifre | Prod'da cluster + mirroring/quorum queues, TLS, şifreler Vault'tan |
| Container kullanıcısı | `appuser` (non-root, uid 1000) | Aynı yaklaşım korunmalı, ek olarak read-only root filesystem düşünülebilir |
| Kafka | Tek broker, replication factor yok | Prod'da min. 3 broker, `replication.factor >= 3`, `min.insync.replicas` ayarı |
| Secrets (appsettings) | Düz metin (sadece demo/dev için) | Connection string ve token'lar Vault/Key Vault'tan runtime'da çekilmeli, repoya asla yazılmamalı |
| Resiliency | Yok (bu modülde kapsam dışı) | Polly ile Retry/Circuit Breaker/Fallback — **Modül 1'in ikinci bölümünde** işlenecek |
| Health checks | `/health` tek endpoint | Prod'da `liveness` ve `readiness` ayrı endpoint'lere bölünmeli (K8s için) |
| Nginx | Basit reverse proxy | Prod'da TLS termination, rate limiting, gzip, request size limit eklenmeli |
| Graceful shutdown | .NET varsayılanı | `SIGTERM` sinyalinde bağlantıların düzgün kapatılması için timeout ayarları gözden geçirilmeli |
| Loglama seviyesi | Development: Debug | Prod'da Information/Warning; PII (kişisel veri) loglanmamasına dikkat |

---

## 9. Sorun Giderme

| Belirti | Olası Neden | Çözüm |
|---|---|---|
| Kafka container sürekli restart oluyor | KRaft konfigürasyonu için yeterli süre geçmedi | `docker compose logs kafka` inceleyin, 30-60 sn bekleyin |
| `Image ... not found` hatası (özellikle Bitnami imajları) | Docker Hub, Bitnami'nin eski/legacy imaj etiketlerini ücretsiz katmandan kaldırdı | Bu şablon artık Kafka için Bitnami yerine resmi `apache/kafka` imajını kullanıyor. Başka bir imaj için de benzer bir hata alırsanız `docker pull <imaj>:latest` ile hangi etiketlerin erişilebilir olduğunu kontrol edin |
| `neominal-seq` sürekli restart oluyor, loglarda `No default admin password was supplied` hatası | Seq'in güncel sürümleri ilk çalıştırmada bir admin şifresi/authentication kararı zorunlu tutuyor | Bu şablonda `SEQ_FIRSTRUN_NOAUTHENTICATION: "True"` env değişkeni zaten eklendi; hâlâ hata alıyorsanız `docker compose down -v` ile eski (bozuk) `seq-data` volume'ünü silip tekrar `up` yapın |
| Uygulama Postgres'e bağlanamıyor | Postgres henüz hazır değil | `docker compose ps` ile `healthy` durumunu bekleyin |
| Vault secret okurken 404 | Secret henüz yazılmadı | Bölüm 3.2'deki `vault kv put` komutunu çalıştırın |
| Prometheus'ta `dotnet-app` hedefi `DOWN` görünüyor | Uygulama henüz ayakta değil veya Linux'ta `host.docker.internal` çözülemiyor | Uygulamanın çalıştığından emin olun; Linux'ta `extra_hosts` ayarının compose dosyasında olduğunu doğrulayın |
| Nginx 502 Bad Gateway | Upstream adresi (host.docker.internal / app) yanlış veya uygulama kapalı | `nginx/nginx.conf` içindeki `upstream` satırını ve uygulamanın çalışır durumda olduğunu kontrol edin |
| `localhost:8080` ve `localhost:8090` aynı şeyi mi gösteriyor kafam karıştı | Hayır — `8080` doğrudan uygulamaya, `8090` nginx üzerinden uygulamaya gider | Aralarındaki farkı görmek için `curl -i http://localhost:8090/demo/errors/bad-request` isteği atıp Seq'te `X-Forwarded-*` header'larının geldiğini kontrol edin |
| Postgres'e bağlanırken `SocketException (10053)` / "bağlantı ana makinenizdeki yazılım tarafından iptal edildi" | Windows'ta antivirüs/firewall SSL handshake'i kesiyor, veya `localhost` IPv6'ya çözülüyor | `appsettings.json`'da connection string'i `Host=127.0.0.1;...;SSL Mode=Disable` şeklinde kullanın (bu şablonda zaten böyle ayarlı); hâlâ sorun varsa antivirüsü geçici kapatıp test edin |
| Yukarıdaki düzeltmelere rağmen Postgres/DBeaver bağlantısı hala `EOFException`/`SocketException` veriyor | Güvenlik yazılımı özellikle standart veritabanı portunu (`5432`) izliyor/kesiyor olabilir | Bu şablon zaten Postgres'i host'ta `15432` portundan yayınlıyor (bkz. Bölüm 3.1'deki not); DBeaver/appsettings'te `15432` kullandığınızdan emin olun |
| `/demo/kafka/publish` isteği uzun süre yanıt vermiyor / timeout düşüyor | Muhtemelen daha önce elle oluşturulmuş, yanlış replication factor'lü bir `demo-events` topic'i kalmış | Kafka UI'da (`http://localhost:8082`) `demo-events` topic'ini silip uygulamayı yeniden başlatın — uygulama açılışta topic'i kendisi doğru ayarlarla (RF=1) yeniden oluşturur |
| Uygulama açılışında Kafka topic'i/consumer group'u görünmüyor | Kafka henüz tam hazır değilken uygulama açılmış olabilir | Konsol/Seq loglarında "Kafka topic ... olusturulamadi (deneme X/5)" uyarısı varsa Kafka'nın `healthy` durumuna geçmesini bekleyip uygulamayı yeniden başlatın |
| Konsol loglarında `rdkafka` ile `Disconnected while requesting ApiVersion` hatası | Güvenlik yazılımı Kafka'nın host'a açık portunu (Postgres'teki `5432` gibi) izleyip kesiyor olabilir | Bu şablon zaten Kafka'nın host portunu `19094` olarak ayarlıyor (bkz. Bölüm 3.1'deki not); `appsettings.json`'da `Kafka:BootstrapServers` değerinin `localhost:19094` olduğundan emin olun |

---

## 10. Klasör Yapısı

```
net-microservices-template/
├── docker-compose.infra.yml       # Tüm altyapı servisleri
├── Neominal.Microservices.Template.sln   # Visual Studio solution dosyası
├── Neominal.Microservices.Template.postman_collection.json   # Postman koleksiyonu (tüm demo endpoint'leri)
├── .env                            # Seq/Grafana admin şifreleri (repoya commit edilmez)
├── .env.example                    # .env için şablon
├── nginx/
│   └── nginx.conf                 # Reverse proxy konfigürasyonu
├── prometheus/
│   └── prometheus.yml             # Prometheus scrape config
├── README.md                      # Bu döküman
└── src/
    └── Neominal.Microservices.Template/
        ├── Program.cs              # Servis wiring (Serilog, OTel, EF, Redis, Kafka, Vault...)
        ├── Properties/launchSettings.json  # dotnet run için varsayılan port (8080)
        ├── Dockerfile               # Multi-stage, non-root build
        ├── appsettings.json         # Host'tan çalıştırma için ayarlar
        ├── appsettings.Container.json  # Container'dan çalıştırma için ayarlar
        ├── Endpoints/DemoEndpoints.cs
        ├── Endpoints/JobsEndpoints.cs      # Hangfire fire-and-forget/scheduled/recurring senaryolari
        ├── Endpoints/RabbitMqEndpoints.cs  # RabbitMQ publish/consume senaryosu
        ├── Infrastructure/AppDbContext.cs
        ├── Infrastructure/DemoJobService.cs   # Hangfire job'larinin calistirdigi is mantigi
        ├── Infrastructure/VaultSecretService.cs
        ├── Infrastructure/KafkaMessageBus.cs
        ├── Infrastructure/KafkaTopicInitializer.cs  # Acilista Kafka topic'ini otomatik olusturur
        ├── Infrastructure/RabbitMqMessageBus.cs  # RabbitMQ publisher/consumer
        ├── Middleware/GlobalExceptionHandler.cs
        └── Validation/CreateProductRequest.cs
```

---

## Not

Bu şablondaki NuGet paket versiyonları eğitim materyali hazırlandığı tarih itibarıyla makul/güncel sürümlerdir. `dotnet restore` sırasında bir sürüm bulunamazsa, `dotnet add package <PaketAdi>` komutuyla en güncel sürüme yükseltmeniz yeterlidir.
