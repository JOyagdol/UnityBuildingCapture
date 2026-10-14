# Unity Research Capture

Unity에서 사람 눈높이로 이동하며 연구용 PNG와 촬영 메타데이터를 저장하는 **코드 전용 패키지**입니다.
WASD 수동 이동과 NavMesh 경로를 따르는 자동 이동을 지원하며, 이동 모드와 자동 촬영 상태를 각각 조작합니다.

**실제 건물 모델, Unity Scene, 지리 좌표, Room 정답, 촬영 이미지, 빌드 실행 파일은 포함하지 않습니다.**
이 저장소만 복제해서는 특정 건물 공간이 나타나지 않습니다. 사용할 권한이 있는 모델과 Scene을 로컬에서 별도로 준비하세요.

## 개발 환경

| 항목 | 기준 버전 |
|---|---|
| Unity Editor | 2022.1.20f1 |
| Universal Render Pipeline | 13.1.8 |
| Cesium for Unity | 1.14.1 |
| Unity Mathematics | 1.2.6 |
| Input System 패키지 | 1.4.3 |
| Active Input Handling | Both 또는 Input Manager (Old) |

키보드·마우스 처리는 `UnityEngine.Input`을 사용합니다. `Mouse X`, `Mouse Y` 축이 필요합니다.
Cesium 타입을 코드에서 참조하므로 일반 정적 메시를 사용할 때도 Cesium 패키지가 설치되어 있어야 합니다.
Built-in/HDRP, Unity 6 및 다른 패키지 버전의 호환성은 검증하지 않았습니다.

## 설치

1. 별도 Unity 2022.1.20f1 URP 프로젝트를 만들거나, 사용할 권한이 있는 로컬 프로젝트를 엽니다.
2. `Edit > Project Settings > Package Manager > Scoped Registries`에 아래 레지스트리를 추가합니다.
   - Name: `Cesium`
   - URL: `https://unity.pkg.cesium.com`
   - Scope: `com.cesium.unity`
3. 이 저장소를 로컬 폴더에 Clone합니다. 실제 건물 프로젝트 내부에 저장소를 만들지 마세요.
4. `Window > Package Manager > + > Add package from disk...`에서 이 저장소의 `package.json`을 선택합니다.
   의존 패키지가 내려받아지고 컴파일됩니다. 이미 사용 중인 패키지 버전과의 충돌은 먼저 확인하세요.
5. `Edit > Project Settings > Player > Other Settings > Active Input Handling`을 `Both`로 설정합니다.
6. 사용할 Scene을 열고 아래 설정을 수행합니다. 이 패키지는 URP Pipeline Asset이나 Scene을 포함하지 않습니다.

이 패키지를 설치하면 `UnityResearchCapture` 네임스페이스의 코드가 추가됩니다.
기존 이동·촬영 Rig가 있는 프로젝트에서는 두 시스템을 동시에 실행하지 마세요.

## 로컬 Scene 연결

1. 모델의 단위와 위쪽 축을 확인합니다. Y-up 좌표계에서 동작하며, `unityUnitsPerMetre`로 실제 축척을 설정합니다.
2. 바닥·벽·가구에 Collider를 준비합니다. Cesium 타일은 `Create Physics Meshes`를 켜세요.
3. 바닥 위 시작점에 빈 GameObject를 만들고 선택합니다. 이 위치는 **눈높이가 아닌 발 위치**입니다.
4. `Tools > Research Capture > Set Up Active Scene`을 실행합니다.
   선택한 Scene 오브젝트의 위치를 시작점으로 사용하며, 선택이 없으면 원점을 사용합니다.
   동일 Scene에 기존 Rig가 있으면 이를 선택하고 중복 생성하지 않습니다. Scene은 직접 저장하세요.
5. `Assets/ResearchCaptureLocal/ResearchCaptureSettings.asset`에서 축척, 눈높이, 속도, 촬영 간격과 해상도를 조절합니다.
6. `CaptureArea`의 BoxCollider 크기, `floorHeight`, `floorTolerance`를 **실제 해당 층의 바닥**에 맞춥니다.
   영역은 월드 Y를 기준으로 하므로 회전하지 않은 박스를 사용하세요. `floorHeight`는 카메라의 고정 높이가 아닙니다.
7. Cesium 의미 정보가 있는 바닥은 `requireCityGmlFloor`를 켜고 `requiredLevel`을 맞춥니다.
   기본 판독기는 `gml_class=bldg_FloorSurface`, `gml_name`, `Constraints.Level`, `Pset_SlabCommon.IsExternal`을 읽습니다.
   데이터마다 속성이 다르면 `CityGmlSurface.cs`를 조정하세요. 명시적으로 만든 정적 바닥에서만 이 검사를 끄세요.
8. 영역이 실내 보행 바닥만 포함하는지 확인한 뒤 `approvedForCollection`을 켭니다.
   지붕·다른 층·건물 외부를 포함하지 않도록 영역과 바닥 높이 범위를 좁게 지정하세요.
9. Play를 누릅니다. 지면 준비 완료 후 버튼이나 단축키로 이동·촬영합니다.

설정 메뉴가 만드는 4×4m 영역은 배치를 위한 기본값입니다. 실제 모델의 보행 가능 영역이라고 판정한 값이 아닙니다.
기존 카메라와 Cesium 이동 컨트롤러는 실행 중 비활성화되며, 원점 이동도 중지합니다.
별도로 작성한 이동 스크립트는 `ResearchWalker.competingControllers`에 지정하세요.

## 조작

기본 상태는 **수동 이동 / 자동 촬영 꺼짐 / 커서 해제**입니다.

| 입력 | 동작 |
|---|---|
| W / A / S / D | 시선의 수평 방향으로 이동 |
| 마우스 | 좌우·상하 시선 회전 |
| Shift | 빠른 이동 |
| F1 | 자동 이동 ↔ 수동 이동 |
| F2 | 자동 촬영 켜기·끄기 |
| P | 현재 시점에서 한 장 촬영 |
| Esc | 커서 잠금 해제 및 화면 버튼 조작 |
| 패널 밖 Game 화면 클릭 | 커서 잠금 및 마우스 시선 조작 |

화면 버튼으로도 자동 이동, 자동 촬영, 한 장 촬영, 보행 경로 재생성을 실행할 수 있습니다.

- **수동 이동 + 자동 촬영:** 자동 촬영을 켠 후 걸으면 이동 거리 기준으로 저장합니다. 정지 중 반복 촬영하지 않습니다.
- **자동 이동 + 자동 촬영:** 두 상태를 각각 켭니다. 지점 도착 → 정지 → 회전 → 촬영 → 다음 지점 순서로 동작합니다.
- **원할 때 한 장:** 자동 촬영을 끄고 P 또는 촬영 버튼을 누릅니다. 두 이동 모드 모두 지원합니다.

## 주요 설정과 경로

| 설정 | 기본값 |
|---|---|
| 눈높이 | 바닥에서 1.6m |
| 걷기 / 빠른 이동 | 1.4 / 3m/s |
| 마우스 감도 / 상하 제한 | 2 / ±80° |
| 자동 방문 지점 간격 | 1m |
| 지점당 촬영 방향 | 8방향, 45° 간격 |
| 수동 이동 중 자동 촬영 거리 | 1m |
| 이미지 | 1920×1080 PNG |

실제 이동은 `CharacterController` 한 개가 담당합니다. NavMesh는 경로 계산에만 사용하므로
NavMeshAgent와 수동 이동 컴포넌트가 동시에 위치를 제어하지 않습니다.
NavMesh는 제한된 바닥과 충돌체를 검사한 뒤 런타임에 생성합니다. 별도 AI Navigation 패키지는 필요하지 않습니다.
도달 불가·정체 지점은 건너뛰며 이유를 기록합니다. 자동 이동을 시작할 수 없으면 현재 위치에서 수동 모드를 유지합니다.
원하는 순서의 지점을 직접 지정하려면 `ResearchNavigation.useInspectorWaypoints`와 `waypoints`를 사용하세요.
각 Transform은 발밑 바닥 위치이며, 직접 지정한 지점도 영역·장애물·도달 가능성 검사를 받습니다.

## 저장 결과

기본 저장 경로는 `Application.persistentDataPath/ResearchCaptures`입니다.
Windows에서는 일반적으로 `%USERPROFILE%/AppData/LocalLow/<CompanyName>/<ProductName>/ResearchCaptures`입니다.
`outputFolder`에 절대 경로를 지정하거나 실행 시 `-capture-output "D:/CaptureOutput"`을 사용할 수 있습니다.
상대 경로는 `Application.persistentDataPath`를 기준으로 해석합니다.

```text
ResearchCaptures/
  <UTC timestamp>_<run GUID>/
    images/<image GUID>.png
    ground_truth/<image GUID>.json
    session.json
    events.jsonl
```

이미지와 JSON이 모두 저장된 경우에만 완료 개수가 증가합니다. 파일명과 실행 폴더는 고유 ID를 사용합니다.
강제 종료 후에는 PNG와 JSON이 모두 존재하는 쌍만 사용하세요.
URP 카메라 렌더링 시점의 위치·회전과 촬영 시각을 기록하며, HUD·Canvas·Editor Gizmo는 촬영에서 제외합니다.
JSON에는 이미지 ID, 파일명, UTC 시각, 프레임, 위치·회전, 단위, 시야각, 투영행렬, 해상도,
이동 모드, 촬영 모드, Room ID, 보조광 설정이 포함됩니다.

**추론 입력에는 `images/`만 전달하고 `ground_truth/`는 평가용으로 분리하세요.**
좌표는 Unity world로 명시합니다. CesiumGeoreference가 있으면 검증 가능한 API 변환을 통해 ECEF(m)도 기록하지만,
원본 CityGML 좌표계와 변환을 확인한 것은 아니므로 원본 CityGML 좌표라고 주장하지 않습니다.

Room ID는 기본 `unknown`입니다. `Tools > Research Capture > Add Room Ground Truth Region`으로
실제 정답을 아는 방의 영역과 ID를 **로컬 Scene에만** 지정하세요. 서로 다른 ID가 겹치면 `unknown`입니다.
균일 보조광 `useAmbientFill`은 어두운 실내를 위한 옵션이며 실제 조도 측정값이 아닙니다. 적용 여부는 JSON에 기록됩니다.

## 파일 구성

| 경로 | 역할 |
|---|---|
| `Runtime/ResearchWalker.cs` | 이동, 충돌, 시선, 모드 전환, 촬영 순서 |
| `Runtime/ResearchNavigation.cs` | 지면 검사, NavMesh, 방문 지점 |
| `Runtime/ResearchCaptureWriter.cs` | 카메라 PNG와 메타데이터 저장 |
| `Runtime/ResearchCaptureSettings.cs` | Inspector 설정 |
| `Runtime/ResearchCaptureHud.cs` | 조작 버튼과 상태 표시 |
| `Runtime/CaptureArea.cs` | 허용 층·수집 영역 |
| `Runtime/CityGmlSurface.cs` | Cesium 의미 정보의 바닥 판독 |
| `Runtime/RoomRegion.cs` | 사용자가 지정한 방 정답 영역 |
| `Runtime/CaptureGizmoScope.cs` | 촬영 중 Editor Gizmo 제외 |
| `Editor/ResearchCaptureSetup.cs` | 데이터에 의존하지 않는 Scene 연결 메뉴 |
| `Tools~/audit_release.py` | 커밋 대상 파일과 제외 규칙 검사 |

## 검증 범위와 제한

이 패키지의 원형은 Unity 2022.1.20f1 / URP 13.1.8 Editor에서 컴파일과 Play Mode 촬영을 확인했습니다.
공유용 분리본에서는 프로젝트 고유 설정 메뉴를 제거하고 범용 메뉴와 별도 어셈블리를 만들었습니다.
분리본의 컴파일·설정 메뉴 검증 결과는 아래에 기록합니다. 원형의 결과를 분리본 전체의 실행 검증으로 간주하지 마세요.

- 분리본 컴파일 및 메뉴 연결: 2026-10-14, Unity 2022.1.20f1에서 통과. 실제 모델이 없는 임시 프로젝트와 설치된 패키지 캐시를 사용했습니다.
- 설정 메뉴 검사 9개 통과: Rig 생성, 선택한 시작점 사용, 컴포넌트 연결, 초기 모드, 영역 승인 기본값, 일반 바닥 설정, 외부 데이터 미포함, 비활성 Rig 중복 방지, Room 기본 unknown.
- 분리본의 Play Mode 이동·PNG 저장은 이번 패키지 분리 작업에서 다시 실행하지 않았습니다.
- Windows 독립 실행 파일의 이미지 저장: 아직 검증 완료하지 못했습니다. 숨김 실행 환경에서는 카메라 렌더 프레임이 발생하지 않았습니다.
- 실제 키보드·마우스 조작, 전체 경로 장시간 순회, 다른 모델·층 연결: 적용하는 프로젝트에서 확인해야 합니다.
- 계단·엘리베이터 등 층간 이동은 자동 구성하지 않습니다.

빌드는 로컬 프로젝트에서 Scene을 저장하고 `File > Build Settings`에 추가한 뒤 수행하세요.
Windows 실행 파일은 Data 폴더와 DLL 등 전체 빌드 결과를 함께 사용합니다. 외부 타일 데이터는 별도로 필요합니다.

## Git 관리 범위

이 저장소는 완성된 Unity 프로젝트가 아닌 **기능 코드 패키지**입니다.
`.gitignore`는 허용한 소스·패키지 설정·README만 포함하는 방식입니다.
Scene, 모델, 텍스처, 실제 지리 좌표, Room 정답, 로컬 설정 에셋, 촬영 결과, 실행 파일과 로그를 추가하지 마세요.
실제 공간을 포함하는 예전 Scene·설정 스크립트·검증 보고서도 복사하지 않습니다.

커밋 전 검사:

```powershell
git add .
python "Tools~/audit_release.py" --staged
git diff --cached --stat
git commit -m "Describe the source change"
git push
```

검사 도구는 파일 유형·알려진 데이터 경로·토큰 패턴을 확인하는 보조 도구입니다.
새 코드에 현장 좌표나 정답을 직접 넣으면 자동 검사가 모두 판별할 수 없으므로 변경 내용도 검토하세요.
`git add -f`로 제외된 데이터를 강제로 추가하지 마세요. `.meta`는 포함된 코드·폴더와 함께 관리합니다.
