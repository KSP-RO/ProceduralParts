ifeq ($(OS),Windows_NT)
	# do 'Doze stuff
else
	UNAME_S := $(shell uname -s)
	ifeq ($(UNAME_S),Linux)
		KSPDIR  := ${HOME}/.local/share/Steam/SteamApps/common/Kerbal\ Space\ Program
		MANAGED := ${KSPDIR}/KSP_Data/Managed/
	endif
	ifeq ($(UNAME_S),Darwin)
		KSPDIR  := ${HOME}/Library/Application\ Support/Steam/SteamApps/common/Kerbal\ Space\ Program/
		MANAGED := ${KSPDIR}/KSP.app/Contents/Data/Managed/
	endif
endif

GAMEDATA	:= ${KSPDIR}/GameData
APIGAMEDATA	:= ${GAMEDATA}/KSPAPIExt
PLUGINDIR	:= ${APIGAMEDATA}/Plugins
KSPAPIEXT	:= ../../KSPAPIExtensions/Source

TARGETS		:= ProceduralParts.dll

API_FILES := \
	DecouplerTweaker.cs \
	ObjectSerializer.cs \
	ProceduralAbstractShape.cs \
	ProceduralAbstractSoRShape.cs \
	ProceduralHeatshield.cs \
	ProceduralPart.cs \
	ProceduralShapeBezierCone.cs \
	ProceduralShapeCone.cs \
	ProceduralShapeCylinder.cs \
	ProceduralShapePill.cs \
	ProceduralSRB.cs \
	TankContentSwitcher.cs \
	TransformFollower.cs \
	zzVersionChecker.cs \
	ICostModifier.cs \
	VectorUtils.cs \
	$e

RESGEN2	:= resgen2
GMCS	:= gmcs
GIT		:= git
TAR		:= tar
ZIP		:= zip

all: version ${TARGETS}

.PHONY: version
version:
	@../version-gen

info:
	@echo "Modular Fuel Tanks Build Information"
	@echo "    resgen2:  ${RESGEN2}"
	@echo "    gmcs:     ${GMCS}"
	@echo "    git:      ${GIT}"
	@echo "    tar:      ${TAR}"
	@echo "    zip:      ${ZIP}"
	@echo "    KSP Data: ${KSPDIR}"
	@echo "    Plugin:   ${PLUGINDIR}"

ProceduralParts.dll: ${API_FILES}
	${GMCS} -unsafe -t:library -lib:${APIEXTDATA},${MANAGED},${KSPAPIEXT} \
		-r:Assembly-CSharp,Assembly-CSharp-firstpass,UnityEngine,KSPAPIExtensions \
		-out:$@ $^

clean:
	rm -f ${TARGETS} AssemblyInfo.cs

install: all
	mkdir -p ${PLUGINDIR}
	cp ${TARGETS} ${PLUGINDIR}

.PHONY: all clean install
