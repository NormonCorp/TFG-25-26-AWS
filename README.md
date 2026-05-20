# WEB LINK
https://dorjeeherblas.github.io/Presigned-URL-AWS-UNITYGAME/
---
# Technical Framework: Player Protection - The Black Box of Logic

The framework titled **"Player Protection: The Black Box of Logic"** establishes a secure-by-design architecture that shifts the authority of game logic and data validation from the player's client device to a controlled serverless environment. Historically, the video game industry—particularly in independent and academic sectors—has relied on client-side validation, which creates structural vulnerabilities such as score manipulation, account theft, and the distribution of unauthorized versions. This project addresses these gaps by applying **distributed computing principles** and a **Zero Trust paradigm**, ensuring that every request is authenticated, authorized, and verified regardless of its origin.

### Technological Implementation and Usage
The system is constructed using an **Amazon Web Services (AWS)** serverless stack, which reduces the attack surface by eliminating permanently exposed server infrastructure. The operational flow is managed through several key layers:

*   **Identity and Perimeter Security:** **Amazon Cognito** manages player identities and issues digitally signed **JSON Web Tokens (JWT)**. **Amazon API Gateway** acts as the secure entry point, performing initial token validation and enforcing rate limiting (throttling) to prevent brute-force attacks and resource abuse.
*   **Business Logic and Validation:** **AWS Lambda** functions execute the core game logic on demand. For instance, the `VerifyPlayerStats` flow ensures data integrity by recalculating **HMAC-SHA256 signatures** on the server side to detect if the client has tampered with game metrics. Similarly, the `VerifyGameHash` function checks the integrity of the game's executable against a table of authorized versions in **Amazon DynamoDB**.
*   **Data Consistency and Distribution:** **Amazon DynamoDB** provides high-performance, NoSQL storage for player profiles and session states, utilizing **TTL (Time To Live)** attributes to automatically purge expired tokens and temporary data. For the secure distribution of assets, the system uses **Amazon S3** with **presigned URLs**, granting players temporary, five-minute access to specific files without exposing the storage bucket publicly.
*   **Observability:** **Amazon CloudWatch** centralizes logging and monitoring, enabling the detection of anomalous patterns, such as repeated failed login attempts or unusual traffic, which can trigger automated defensive responses like account suspension or IP blocking.

### Authorship and Direction
This Final Degree Project (TFG) was developed at the **Universidad Complutense de Madrid** by authors **Alberto Peñalba Martos** and **Dorje Khampa Herrezuelo Blasco**, under the direction of **José Luis Vázquez Poletti** and **David Pacios Izquierdo**.
---

