sc.exe create FORMS_APP binPath= "C:\apps\Forms-app.exe"
sc.exe description TECH_NL_FORMS "This is the Tech NL Forms application." 
sc.exe failure TECH_NL_FORMS reset= 3600 actions= restart/60000/restart/60000/restart/60000
sc.exe start TECH_NL_FORMS 
sc.exe config TECH_NL_FORMS start=auto
TIMEOUT /T 2