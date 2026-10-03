# 맵 패턴 제작

## 폴더와 기본 규칙

`Assets/Resources/MapPatterns` 아래에 MapPattern 컴포넌트가 붙은 프리팹을 저장하면 다음 게임 시작 시 자동으로 불러옵니다. 하위 폴더도 검색합니다. 실행 중 파일을 추가하는 방식은 아니며, 빌드에는 Resources의 프리팹이 포함됩니다.

- 가로: x = -4~4, 총 9칸.
- 세로: y = 0~Length-1, 6~30행.
- 첫 행과 마지막 행: 모든 칸이 안전하고 장애물이 없는 연결 행.
- Ground와 Structures: 프리팹 루트 Grid 바로 아래의 별도 Tilemap.
- Grid: 사각형, Cell Size (1, 1, 1), Cell Gap (0, 0, 0). Tilemap의 로컬 위치/회전/크기는 기본값을 유지합니다.
- Ground는 전체 9×Length 칸을 채웁니다. 용암 행에도 바닥 타일이 필요합니다.

프리팹은 배치 데이터로 읽고 기존 게임의 Tilemap에 적용합니다. 프리팹 전체를 씬에 인스턴스화하지 않습니다. 게임별 수집, 이동, 트리거 실행 상태는 원본 프리팹과 분리되어 있습니다.

## 만드는 방법

1. 샘플 프리팹을 복제하거나 Project 메뉴의 `Assets > Create > CrossTheBoard > Map Pattern`으로 생성합니다.
2. 루트의 MapPattern에서 Length와 Theme Id를 지정합니다. 길이를 바꾸면 `Resize / Fill Ground` 버튼으로 바닥을 맞춥니다. 기존 범위의 바닥 타일은 보존합니다.
3. Tile Palette로 Ground를 칠하고 Structures에 고정 장애물을 칠합니다. 코인이나 보상은 칠하지 않습니다.
4. Lava Rows에 로컬 행 번호와 징검다리 x 좌표를 지정합니다.
5. Moving Obstacles에 시작 좌표, Tile, Movement, Step Interval을 지정합니다. Patrol은 Direction과 Distance도 설정합니다. Chase는 안전한 경로를 따라 플레이어 쪽으로 이동합니다.
6. Row Triggers에 프리팹 내에서 고유한 ID와 로컬 행 번호를 지정합니다.
7. `Validate Pattern` 버튼으로 확인하고 프리팹을 저장합니다. 씬/프리팹 뷰에서 루트를 선택하고 Gizmos를 켜면 용암·징검다리와 이동 장애물 위치/순찰 범위가 표시됩니다.

잘못된 길이, 겹치는 장애물, 막힌 경로, 중복 트리거는 게임 시작 시에도 오류로 거부됩니다. 단순히 입구에서 출구까지 길이 있는 것으로 충분하지 않습니다. 뒤로 최대 한 칸만 이동할 수 있으므로, 인접한 두 행의 안전한 칸들이 서로 연결되어야 합니다.

## 랜덤 생성

같은 Theme Id의 프리팹 중 하나를 선택해 1행부터 연속 배치합니다. 0행과 -1행은 시작용 안전 행으로 유지합니다. MapManager의 Repeat Weight 기본값은 0.15입니다. 직전 프리팹만 0.15, 다른 프리팹은 각각 1의 가중치를 사용하므로 같은 테마의 프리팹이 두 개일 때 즉시 반복 확률은 약 13%입니다. 후보가 하나라면 그 프리팹을 반복합니다.

Coin Chance와 Reward Chance에 따라 도달 가능한 빈 칸에 코인과 특수 보상을 배치합니다. 둘은 장애물, 용암, 특수 칸 및 서로와 겹치지 않습니다. 현재 특수 보상은 파란색 보너스 타일이며 Reward Points만큼 ItemScore를 올립니다. 기본값은 100점이고 지갑 코인으로 지급하지 않습니다. Coin Seed를 지정하면 같은 플레이 순서에서 패턴과 수집물 배치를 재현할 수 있습니다.

이동 장애물도 수집물·특수 칸과 겹치지 않고 경로를 막지 않는 이동만 실행합니다. 기존 장애물 규칙에 맞춰 플레이어 칸을 덮어쓰지 않으며, 추격 장애물은 인접 칸에서 멈춥니다. 용암의 징검다리 밖으로 이동하면 게임이 종료됩니다.

## 테마와 트리거

숲 테마 ID는 `meadow`, 화산 테마 ID는 `volcano`입니다. 새 테마는 해당 Theme Id를 가진 프리팹을 같은 폴더 아래 추가하면 됩니다. 테마 외형은 각 프리팹의 Ground 타일과 Tilemap Color, Lava Color, Stepping Stone Color로 설정합니다. 현재 씬의 Initial Theme Id는 `meadow`입니다.

테마 변경은 다음 중 하나로 요청합니다.

- Row Trigger의 Next Theme Id에 대상 테마를 지정합니다.
- 게임 코드에서 `MapManager.Instance.RequestThemeChange("volcano")`를 호출합니다.

요청 시 현재 프리팹은 끝까지 유지합니다. 이미 미리 생성된 다음 프리팹도 미방문 경계 뒤에서 교체합니다. 실제 CurrentThemeId와 ThemeChanged 이벤트는 플레이어가 새 프리팹에 진입할 때 갱신되며, 경계를 한 칸 후퇴했다 돌아와도 다시 발동하지 않습니다.

RowTriggered 이벤트의 ID는 `배치 시작 행/프리팹 이름/작성한 ID` 형식입니다. 같은 프리팹이 반복돼도 배치별로 한 번씩 실행됩니다. 프리팹의 씬 오브젝트에 이벤트를 연결하는 대신, 런타임 맵의 RowTriggered를 구독해 실제 게임 오브젝트를 제어하세요.

## 샘플

| 프리팹 | 길이 | 포함 내용 |
| --- | --- | --- |
| meadow/Meadow_06 | 6행 | 고정 장애물, 순찰 장애물, 체크포인트 행 |
| meadow/Meadow_12 | 12행 | 용암·징검다리, 순찰·추격 장애물, 화산 전환 요청 행 |
| volcano/Volcano_18 | 18행 | 연속 용암 행, 순찰·추격 장애물, 체크포인트 행 |
| volcano/Volcano_30 | 30행 | 여러 용암 행, 순찰·추격 장애물, 숲 전환 요청 행 |

## 검증

기존 GameplayRegressionChecks/CollectionRegressionChecks와 MapPatternRegressionChecks를 Unity 배치 모드로 실행합니다. 저장 검사는 회사명이 CodexValidation인 별도 임시 프로젝트에서만 실행하도록 제한되어 있습니다. 샘플 생성용 CreateSamples는 기존 프리팹을 덮어쓰지 않습니다.
