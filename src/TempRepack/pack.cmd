
FOR /R Data\Build\ %%G IN (*.nuspec) DO (
  call nuget pack %%G -NoPackageAnalysis -OutputDirectory Data\Build
)
