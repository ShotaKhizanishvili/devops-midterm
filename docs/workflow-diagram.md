# სამუშაო პროცესის დიაგრამა (Workflow Diagram)

ქვემოთ მოცემული დიაგრამები რენდერდება GitHub-ზე ავტომატურად (Mermaid).

## 1. მთლიანი DevOps ციკლი — კოდიდან მონიტორინგამდე

```mermaid
flowchart LR
    subgraph DEV["დეველოპერი"]
        A["კოდის შეცვლა<br/>dev ბრენჩზე"]
        B["git commit<br/>+ git push"]
    end

    subgraph GIT["GitHub (ვერსიების კონტროლი)"]
        C["dev branch"]
        D["Pull Request<br/>dev -> main"]
        E["main branch"]
    end

    subgraph CI["GitHub Actions - CI/CD Pipeline"]
        F["STAGE 1<br/>BUILD<br/>restore + build + publish"]
        G["STAGE 2<br/>TEST<br/>67 xunit ტესტი"]
        H["STAGE 3<br/>DEPLOY<br/>blue-green + health check"]
    end

    subgraph PROD["Production (ლოკალური გარემო)"]
        I["blue slot"]
        J["green slot"]
        K["http://localhost:5080"]
    end

    subgraph MON["მონიტორინგი"]
        L["health-check.ps1"]
        M["health-check.log"]
        N["/Home/Status<br/>დეშბორდი"]
    end

    A --> B --> C
    C -->|push| F
    C --> D
    D -->|PR: build + test only| F
    D -->|merge| E
    E -->|push| F
    F -->|artifact| G
    G -->|tests pass| H
    H --> I
    H --> J
    I --> K
    J --> K
    K --> L --> M --> N
    L -. "DOWN x3" .-> O["rollback.ps1"]
    O -. "აღადგენს წინა რელიზს" .-> K
```

## 2. Blue-Green დეპლოიმენტი და Rollback

```mermaid
sequenceDiagram
    participant CI as GitHub Actions
    participant D as deploy.ps1
    participant BLUE as blue slot (live)
    participant GREEN as green slot (idle)
    participant HC as health-check.ps1

    Note over BLUE: რელიზი N ემსახურება :5080-ს

    CI->>D: deploy.ps1 -ArtifactPath ./artifact
    D->>GREEN: 1. ახალი რელიზის განთავსება idle სლოტში
    D->>GREEN: 2. smoke test :5090-ზე (დროებითი პორტი)

    alt smoke test ჩავარდა
        D-->>CI: exit 1 — blue რჩება live, production ხელშეუხებელი
    else smoke test წარმატებული
        D->>BLUE: 3. live პროცესის გაჩერება
        D->>GREEN: 4. გაშვება :5080-ზე (ტრაფიკის გადართვა)
        D->>HC: 5. /health შემოწმება

        alt /health ჩავარდა
            D->>BLUE: rollback.ps1 — blue ისევ live-ზე
            D-->>CI: exit 1
        else /health OK
            D-->>CI: exit 0 — რელიზი N+1 live, blue = rollback target
        end
    end
```

## 3. Health Check-ის ლოგიკა (ავტომატური Rollback)

```mermaid
stateDiagram-v2
    [*] --> Probe

    Probe: GET /health
    Probe --> LogUp: HTTP 200
    Probe --> LogDown: timeout / 5xx

    LogUp: ჩაწერა ლოგში (UP)
    LogDown: ჩაწერა ლოგში (DOWN)

    LogUp --> ResetCounter
    ResetCounter: ზედიზედ ჩავარდნა = 0
    ResetCounter --> Wait

    LogDown --> Increment
    Increment: ზედიზედ ჩავარდნა += 1
    Increment --> CheckThreshold

    CheckThreshold --> Rollback: >= FailureThreshold და -AutoRollback
    CheckThreshold --> Wait: ლიმიტი არ მიღწეულა

    Rollback: rollback.ps1 — წინა სლოტის აღდგენა
    Rollback --> Wait

    Wait: IntervalSeconds პაუზა
    Wait --> Probe: დარჩა მცდელობები
    Wait --> [*]: მცდელობები ამოიწურა
```
