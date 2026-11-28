# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

require 'tmpdir'
require_relative 'check-workflow-actions'

pin = 'actions/checkout@' + 'a' * 40
ids = ' owner=44036562 repository=197814629'
Dir.mktmpdir('workflow-actions') do |directory|
  File.write(File.join(directory, 'actions.allow'), "# Approved exact revisions\n#{pin}#{ids} # v1.0.0\n")
  path = File.join(directory, 'test.yaml')
  cases = {
    'block step' => ["jobs:\n  test:\n    steps:\n      - uses: #{pin}\n", true],
    'flow and quoted key' => ["jobs: {test: {steps: [{'uses': '#{pin}'}]}}\n", true],
    'folded value' => ["jobs:\n  test:\n    uses: >-\n      #{pin}\n", true],
    'script is data' => ["jobs:\n  test:\n    steps:\n      - run: |\n          echo 'uses: unapproved/action@v1'\n# uses: ignored@v1\n", true],
    'unlisted revision' => ["jobs: {test: {uses: 'actions/checkout@#{'b' * 40}'}}", false],
    'unlisted action' => ["jobs: {test: {uses: 'other/action@#{'a' * 40}'}}", false],
    'version tag' => ["jobs: {test: {uses: actions/checkout@v5}}", false],
    'short hash' => ["jobs: {test: {uses: actions/checkout@aaaaaaa}}", false],
    'expression' => ['jobs: {test: {uses: "${{ inputs.action }}"}}', false],
    'local action' => ['jobs: {test: {uses: ./local}}', false],
    'reusable workflow beside it' => ['jobs: {test: {uses: ./.github/workflows/test.yaml}}', true],
    'missing reusable workflow' => ['jobs: {test: {uses: ./.github/workflows/other.yml}}', false],
    'reusable workflow elsewhere' => ['jobs: {test: {uses: ./.github/workflows/../test.yaml}}', false],
    'remote reusable workflow by tag' => ['jobs: {test: {uses: other/repo/.github/workflows/x.yml@v1}}', false],
    'container action' => ['jobs: {test: {uses: "docker://image:latest"}}', false],
    'alias value' => ["pin: &pin #{pin}\njobs: {test: {uses: *pin}}", false],
    'hidden unlisted anchor' => ['steps: &steps [{uses: unapproved/action@v1}]' + "\njobs: {test: {steps: *steps}}", false],
    'duplicate uses key' => ["jobs: {test: {uses: '#{pin}', uses: unapproved/action@v1}}", false],
    'invalid YAML' => ['jobs: [', false],
  }
  cases.each do |name, (yaml, passes)|
    File.write(path, yaml)
    errors = WorkflowActions.check(directory)
    raise "#{name}: expected #{passes ? 'pass' : 'failure'}, got #{errors.inspect}" unless errors.empty? == passes
    puts "PASS #{name}"
  end
  File.write(path, "jobs: {test: {uses: '#{pin}'}}")
  ['other/action', 'actions-lookalike/checkout', 'NuGet/other', 'NuGet/login/other'].each do |action|
    File.write(File.join(directory, 'actions.allow'), "#{pin}#{ids}\n#{action}@#{'a' * 40} owner=1 repository=2\n")
    raise "Unpermitted owner passed: #{action}" if WorkflowActions.check(directory).empty?
  end
  File.write(File.join(directory, 'actions.allow'), "#{pin}#{ids}\ngithub/codeql-action/init@#{'a' * 40} owner=9919 repository=3\nNuGet/login@#{'b' * 40} owner=968310 repository=4\n")
  raise 'Permitted owners failed' unless WorkflowActions.check(directory).empty?
  File.write(File.join(directory, 'actions.allow'), "actions/checkout@v5#{ids}\n")
  raise 'Unpinned allow-list entry passed' if WorkflowActions.check(directory).empty?

  # CI-08: every entry records its owner's and repository's ids, positive integers, the same for every entry of a repository.
  {
    'ids missing' => "#{pin}\n",
    'repository id missing' => "#{pin} owner=44036562\n",
    'ids in the wrong order' => "#{pin} repository=197814629 owner=44036562\n",
    'id zero' => "#{pin} owner=0 repository=197814629\n",
    'id not a number' => "#{pin} owner=actions repository=197814629\n",
    'ids only in a comment' => "#{pin} # owner=44036562 repository=197814629\n",
    'trailing text' => "#{pin}#{ids} extra\n",
    'another revision with other repository id' => "#{pin}#{ids}\nactions/checkout@#{'c' * 40} owner=44036562 repository=5\n",
    'another action with other owner id' => "#{pin}#{ids}\nactions/cache@#{'c' * 40} owner=6 repository=7\n",
  }.each do |name, list|
    File.write(File.join(directory, 'actions.allow'), list)
    raise "#{name}: passed" if WorkflowActions.check(directory).empty?
    puts "PASS #{name}"
  end
  File.write(File.join(directory, 'actions.allow'), "#{pin}#{ids}\nactions/checkout@#{'c' * 40}#{ids}\nactions/cache@#{'d' * 40} owner=44036562 repository=8\n")
  raise 'Consistent ids failed' unless WorkflowActions.check(directory).empty?

  # The live check compares the recorded ids with the repository GitHub serves under the name, then finds the pinned commit there.
  File.write(File.join(directory, 'actions.allow'), "#{pin}#{ids}\n")
  served = { 'full_name' => 'actions/checkout', 'id' => 197814629, 'owner' => { 'id' => 44036562 } }
  identity_cases = {
    'served as recorded' => [served, { 'sha' => 'a' * 40 }, true],
    're-created under the name' => [served.merge('id' => 900), { 'sha' => 'a' * 40 }, false],
    'owner replaced' => [served.merge('owner' => { 'id' => 901 }), { 'sha' => 'a' * 40 }, false],
    'renamed or transferred' => [nil, { 'sha' => 'a' * 40 }, false],
    'served under another name' => [served.merge('full_name' => 'other/checkout'), { 'sha' => 'a' * 40 }, false],
    'commit absent' => [served, nil, false],
  }
  identity_cases.each do |name, (repository, commit, passes)|
    asked = []
    fetch = lambda do |api|
      asked << api
      api.include?('/commits/') ? commit : repository
    end
    errors = WorkflowActions.check_identities(directory, fetch)
    raise "#{name}: expected #{passes ? 'pass' : 'failure'}, got #{errors.inspect}" unless errors.empty? == passes
    raise "#{name}: asked #{asked.inspect}" unless asked.first == 'repos/actions/checkout'
    puts "PASS identity #{name}"
  end
  File.write(File.join(directory, 'actions.allow'), "#{pin}\n")
  raise 'Identity check without ids passed' if WorkflowActions.check_identities(directory, ->(_) { raise 'no fetch expected' }).empty?

  File.delete(File.join(directory, 'actions.allow'))
  raise 'Missing allow-list passed' if WorkflowActions.check(directory).empty?
end

errors = WorkflowActions.check(File.expand_path('../workflows', __dir__))
raise errors.join("\n") unless errors.empty?
puts 'PASS repository workflows'

package = Psych.safe_load(File.read(File.expand_path('../workflows/package.yml', __dir__)))
steps = package.fetch('jobs').fetch('package').fetch('steps')
upload = steps.index do |step|
  step.fetch('uses', '').start_with?('actions/upload-artifact@') && step.dig('with', 'path') == 'dist'
end
smoke = steps.index { |step| step.fetch('run', '').include?('tools/examples/smoke.py') }
raise 'The package job must upload dist before running the examples smoke' unless upload && smoke && upload < smoke
puts 'PASS package artifact precedes the examples smoke'
