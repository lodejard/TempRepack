
FOR /R Data\Build\ %%G IN (*.nupkg) DO (
  call nuget push %%G -Source https://www.myget.org/F/tempruntime/ -ApiKey %API_KEY%
)
