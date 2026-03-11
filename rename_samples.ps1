# 1. 重命名文件夹
Rename-Item -Path "Samples" -NewName "Samples~"

# 2. 删除旧的 .meta 文件（Unity 会把 Samples~ 整体忽略，meta 文件无效）
Remove-Item -Path "Samples.meta" -ErrorAction SilentlyContinue

# 3. 让 git 追踪变更
git add -A

# 4. 提交
git commit --trailer "Made-with: Cursor" -m "refactor: move Samples to Samples~ (UPM standard)"
