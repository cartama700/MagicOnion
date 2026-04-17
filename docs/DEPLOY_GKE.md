# GKE 배포 가이드

> Aiming PoC Sync Server 를 **GKE Autopilot** 에 올려 부하 시연까지 가는 최단 경로.
> 비용을 최소화하기 위해 `e2-small` 노드 + Spot 인스턴스 권장.

## 1. 사전 준비

```bash
gcloud auth login
gcloud config set project YOUR_PROJECT
gcloud services enable container.googleapis.com artifactregistry.googleapis.com
```

Artifact Registry 리포지토리:

```bash
gcloud artifacts repositories create aiming-poc \
  --repository-format=docker --location=asia-northeast1
```

## 2. 이미지 빌드 & 푸시

```bash
REGISTRY=asia-northeast1-docker.pkg.dev/YOUR_PROJECT/aiming-poc

# Mac 에서 빌드 시 멀티아키 권장
docker buildx build --platform linux/amd64 \
  -f Server/Dockerfile      -t $REGISTRY/aiming-server:v1 --push .
docker buildx build --platform linux/amd64 \
  -f BotClients/Dockerfile  -t $REGISTRY/aiming-bots:v1   --push .
```

## 3. GKE Autopilot 클러스터

```bash
gcloud container clusters create-auto aiming-poc \
  --region=asia-northeast1
gcloud container clusters get-credentials aiming-poc \
  --region=asia-northeast1
```

## 4. 매니페스트 적용

`k8s/server.yaml` 과 `k8s/bots-job.yaml` 의 `REGISTRY/...` 부분을 위에서 푸시한 태그로 교체.

```bash
kubectl apply -f k8s/namespace.yaml
kubectl apply -f k8s/redis.yaml
kubectl apply -f k8s/mysql.yaml
kubectl apply -f k8s/server.yaml
kubectl apply -f k8s/server-hpa.yaml
```

준비 상태 확인:

```bash
kubectl -n aiming-poc get pods -w
kubectl -n aiming-poc get svc server-public   # EXTERNAL-IP 가 잡힐 때까지 대기
```

## 5. 부하 주입

```bash
kubectl apply -f k8s/bots-job.yaml
kubectl -n aiming-poc logs -f job/bots-load
```

## 6. 시연 / 관측

- 대시보드: `http://EXTERNAL-IP/` (포트 80 → 컨테이너 5050)
- HPA 동작: `kubectl -n aiming-poc get hpa server-hpa -w`
- Pod CPU/메모리: `kubectl -n aiming-poc top pods`
- Redis 랭킹: `kubectl -n aiming-poc exec -it deploy/redis -- redis-cli ZRANGE lb:room-00 0 9 WITHSCORES`
- MySQL 매치 기록:
  ```bash
  kubectl -n aiming-poc exec -it deploy/mysql -- \
    mysql -uaiming -paiming aiming -e \
      'SELECT room_id, COUNT(*), AVG(score) FROM match_record WHERE left_at IS NOT NULL GROUP BY room_id;'
  ```

## 7. 정리

```bash
kubectl delete -f k8s/  # 또는 클러스터째
gcloud container clusters delete aiming-poc --region=asia-northeast1
```

## 8. 시연용 체크리스트

- [ ] Pod 1개로 시작 → 봇 수 늘려가며 HPA 가 스케일아웃 하는 것 보여주기
- [ ] `/api/optimize?on=true` 토글 후 `kubectl top pods` 의 CPU 가 떨어지는 것 시연
- [ ] Redis ZRANGE 결과와 대시보드 Top-10 패널이 일치하는지 확인
- [ ] `match_record` 테이블 쿼리로 영속화 동작 입증
