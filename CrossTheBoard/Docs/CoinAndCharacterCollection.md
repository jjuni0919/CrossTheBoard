# 코인 · 캐릭터 컬렉션

## 동작

- 메인 메뉴 좌측 상단에 저장된 코인 잔액을 표시한다. 하단 메뉴는 홈 / 캐릭터 / 상점 / 도전 / 업적의 5칸이다.
- 캐릭터 화면에 10개를 모두 표시한다. 잠긴 카드는 어두운 색과 해금 조건을 표시하고 클릭할 수 없다.
- 해금된 카드를 클릭하면 위쪽 이미지가 바뀐다. 이미지 아래 선택 버튼을 눌러야 저장된 다음 게임 캐릭터가 변경된다.
- 상점 구매는 잔액 차감과 해금을 한 번에 저장한다. 중복 구매, 부족한 잔액, 저장 불가 상태는 차감되지 않는다.
- 임시 이미지는 코드로 생성한 픽셀 스프라이트다. 캐릭터 외형만 다르고 현재 이동/능력치는 동일하다.
- 외형은 캐릭터에 종속된다. 별도의 스킨 구매·장착 기능은 없다.

## 임시 캐릭터

| ID | 이름 | 해금 조건 |
| --- | --- | --- |
| slime | 초록 슬라임 | 기본 해금 |
| robot | 탐험 로봇 | 상점 구매, 50 코인 |
| cat | 별빛 고양이 | 상점 구매, 100 코인 |
| knight | 작은 기사 | 상점 구매, 150 코인 |
| fox | 사막 여우 | 상점 구매, 200 코인 |
| frog | 길잡이 개구리 | 한 게임 최대 10칸 전진 |
| ghost | 달빛 유령 | 한 게임 최대 25칸 전진 |
| mushroom | 황금 버섯 | 맵 코인 누적 25개 수집 |
| pumpkin | 호박 마법사 | 맵 코인 누적 100개 수집 |
| golem | 이끼 골렘 | 모든 게임 합계 100칸 전진 |

누적 수집량은 보유 잔액과 분리되어 구매로 줄어들지 않는다. 업적 보상 코인은 맵 수집 조건에 포함하지 않는다.
진행 조건은 앞으로 처음 도달한 행 기준이다. 후진, 복귀, 좌우 이동으로 전진 기록을 중복 획득하지 않는다.

## 맵 콘텐츠 확장

`MapManager`의 Inspector에서 Coin Chance(기본 0.14), Coin Seed(0은 매 게임 임의값), Obstacle Chance(기본 0.16), Hazard Rows, Cell Triggers, Row Triggers를 설정한다.
시작 행 0과 뒤쪽 행 -1에는 코인을 생성하지 않는다. 칸당 기본 1코인, 수집 보너스 기본 10점이며 GameplayController에서 변경 가능하다.
기존 전진 100점과 코인 보너스는 분리된다. 코인 수집/진행도를 저장한 뒤 코인 타일을 제거한다. 실패하면 그대로 두고 다음 이동에서 재시도한다.
생성된 행은 다시 불러도 랜덤 생성하지 않으므로 같은 게임에서 수집한 코인이 부활하지 않는다.

```csharp
// 장애물은 구조물 Tilemap을 직접 변경하지 말고 이 API로 등록한다.
bool placed = map.TrySetObstacle(new Vector2Int(2, 5), obstacleTile);

// 특정 칸 도달 이벤트. 코인/장애물과 겹치는 등록은 false.
var cell = new CellTriggerDefinition { id = "treasure", position = new Vector2Int(1, 8) };
cell.onReached.AddListener(OpenTreasure);
map.RegisterCellTrigger(cell);

// 행 도달 이벤트는 해당 행의 어느 x좌표에서도 발동한다.
var row = new RowTriggerDefinition { id = "boss_intro", row = 30 };
row.onReached.AddListener(ShowBossIntro);
map.RegisterRowTrigger(row);

// 일반적인 첫 행 도달 알림 및 ID 기반 이벤트를 구독할 수도 있다.
map.RowReached += rowNumber => Debug.Log($"First reached row {rowNumber}");
map.RowTriggered += (id, rowNumber) => Debug.Log($"{id}: {rowNumber}");
```

등록은 가능한 한 초기 코인 생성 전에 한다. MapManager에 직렬화된 트리거는 생성 전에 예약된다.
칸 트리거/행 트리거의 ID는 각각 고유해야 한다. 기본 `oncePerRun = true`라 후진·복귀·좌우 이동으로 재발동하지 않는다.
반복 이벤트가 필요하면 false로 설정한다. 행 이벤트는 칸을 점유하지 않으므로 행의 다른 칸에 코인이 있어도 된다.
각 칸의 코인/장애물/칸 트리거는 API에서 상호 배타적이다. 구조물 Tilemap을 외부에서 직접 쓰면 이 검증을 우회하므로 금지한다.
장애물은 빈 안전 칸에 랜덤 배치된다. 각 인접한 두 행의 안전 칸들이 연결되어야 배치가 승인되므로, 전진이 막히거나 코인에 접근하려고 두 칸 이상 후진해야 하는 지형을 만들지 않는다.
배치와 위치 변경은 통로 및 코인·트리거 접근성을 검사하고 실패하면 원래 위치로 돌아간다. 씬에 직접 그린 지형이나 트리거가 잘못되면 게임 시작 시 오류를 알린다.

## 카메라와 하단 영역

맵은 x=-4..4의 9열, 최고 도달 행 기준 뒤 1칸 / 현재 1칸 / 앞 9칸의 11행이다. 기존 후진·복귀 시 카메라 고정 규칙은 유지한다.
게임 화면을 위쪽에 맞추고 남는 하단은 비운다. `GameplayScene > Gameplay Layout > Bottom Area`의 RectTransform Anchor Max Y로 빈 공간의 최소 높이를 조절한다. 기본값은 0.3125이며 해당 오브젝트의 자식으로 UI를 배치할 수 있다. 화면 비율에 따라 추가 여백이 생길 수 있지만 칸 비율은 변하지 않는다.

## 위험 행과 위치 변경

Hazard Rows에서 행 번호와 Stepping Stone Columns를 지정한다. 씬의 기본 예시는 5행(0, 2), 12행(-2, 0)이다.
위험 행은 파란색, 안전한 징검다리는 밝은 색으로 표시한다. 위험 칸에 진입하면 GameOver이며 점수·코인·칸 이벤트는 지급하지 않는다. 코인과 칸 트리거는 징검다리 또는 일반 안전 칸에만 배치된다.
연속된 위험 행은 적어도 하나의 같은 열 징검다리로 이어져야 한다. 모든 징검다리를 장애물로 막을 수 없다.

```csharp
// 기존 행 이벤트에서 장애물을 소환하고, 이후 이동 타이머에서 호출한다.
map.TrySetObstacle(spawnPosition, obstacleTile);
if (map.TryGetNextStep(obstaclePosition, player.Position, out var next) &&
    map.TryMoveObstacle(obstaclePosition, next))
    obstaclePosition = next;

// 같은 위험 행에서 인접한 칸으로 이동시킨다. 승인된 경우에만 위치를 갱신한다.
map.TryMoveSteppingStone(currentStonePosition, currentStonePosition + Vector2Int.right);
```

경로 탐색은 현재 로드된 안전 칸과 정적 장애물을 사용하며, 추적 장애물 자신의 점유 칸에서 출발할 수 있다. 플레이어 위치에 장애물을 겹쳐 놓거나 코인·트리거를 덮는 이동은 거부된다.
행별 소환 시점은 기존 Row Triggers의 1회 이벤트를 사용한다. 이동 속도·AI·애니메이션은 여기서 추가하지 않고, 경로 탐색과 검증된 한 칸 이동 API만 제공한다.
플레이어·코인·트리거·장애물이 올라가 있는 징검다리를 옮기는 요청은 거부한다. 마지막 통로가 끊기는 이동도 거부하고 색과 안전 판정을 유지한다. 플레이어를 태우는 이동이나 애니메이션은 아직 구현하지 않는다.

## 저장 및 확장

SaveData v3에는 기존 잔액·음량·업적 외에 해금 캐릭터 ID, 선택 캐릭터 ID, 누적 맵 코인, 누적 전진, 최대 전진 거리를 저장한다.
v1 파일은 기존 정보를 보존하면서 기본 캐릭터만 지급하여 읽는다. v2 파일의 스킨 필드는 무시하되 잔액·캐릭터·진행 기록은 보존한다. 폐기된 스킨에 대한 자동 환불은 하지 않는다. 미래 버전 또는 손상된 파일을 기본값으로 덮어쓰지 않는다.
새로운 구매/선택/진행 변경에는 `SaveManager.TryUpdate(...)`를 사용한다. 저장 실패 시 원래 데이터로 돌아간다.
UI는 `DataChanged`를 구독하므로 맵 수집, 업적 보상, 상점 구매가 같은 잔액 표시를 갱신한다.
캐릭터의 안정적인 ID는 변경하지 않는다. 추후 캐릭터별 레벨/스킬/능력치는 기존 `CharacterProgression.md` 설계대로 별도의 ID 기반 진행 데이터로 확장한다.

## 검증

- `GameplayRegressionChecks.Run`: 9×11칸 맵, 원점 시작, 후진 제한, 전진 전용 카메라, 중복 점수 방지, 하단 영역 변경, 통로 보장, 위험 행·징검다리, 움직이는 장애물 경로와 행 소환, 여러 시드에서 실제 이동 검증.
- `CollectionRegressionChecks.Run`: 구매/잔액, 중복/부족 구매 거부, v1/v2 변환, 저장 불가 상태, 재로드, 코인/장애물/트리거 배타성, 행 이벤트, 플레이 해금, 미리보기/확정, 상점의 스킨 UI 제거.
- 컬렉션 테스트는 실제 사용자 세이브를 보호하기 위해 companyName이 `CodexValidation`인 별도 프로젝트에서만 실행한다.
