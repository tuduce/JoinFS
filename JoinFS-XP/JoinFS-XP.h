// The following ifdef block is the standard way of creating macros which make exporting 
// from a DLL simpler. All files within this DLL are compiled with the JOINFSXP_EXPORTS
// symbol defined on the command line. This symbol should not be defined on any project
// that uses this DLL. This way any other project whose source files include this file see 
// JOINFSXP_API functions as being imported from a DLL, whereas this DLL sees symbols
// defined with this macro as being exported.
#ifdef JOINFSXP_EXPORTS
#define JOINFSXP_API __declspec(dllexport)
#else
#define JOINFSXP_API __declspec(dllimport)
#endif

// This class is exported from the JoinFS-XP.dll
class JOINFSXP_API CJoinFSXP {
public:
	CJoinFSXP(void);
	// TODO: add your methods here.
};

extern JOINFSXP_API int nJoinFSXP;

JOINFSXP_API int fnJoinFSXP(void);
