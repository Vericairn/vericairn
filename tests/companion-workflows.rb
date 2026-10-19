# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

require 'minitest/autorun'
require 'tmpdir'
require 'fileutils'
require 'open3'
require 'json'
require 'digest'

class CompanionWorkflowTests < Minitest::Test
  def test_the_existing_parser_observes_quoted_flow_and_folded_uses_but_not_script_text
    Dir.mktmpdir('companion-workflows-') do |root|
      FileUtils.mkdir_p(File.join(root, '.github/scripts'))
      workflows = File.join(root, '.github/workflows')
      FileUtils.mkdir_p(workflows)
      FileUtils.cp(File.expand_path('../.github/scripts/check-workflow-actions.rb', __dir__), File.join(root, '.github/scripts'))
      commit = 'a' * 40
      action = "example/action@#{commit}"
      File.write(File.join(workflows, 'actions.allow'), "#{action} # fixture\n")
      File.write(File.join(workflows, 'ci.yml'), <<~YAML)
        jobs:
          build:
            steps:
              - {'uses': '#{action}'}
              - uses: >-
                  #{action}
              - run: |
                  uses: fake/action@main
      YAML
      output, error, status = Open3.capture3('ruby', File.expand_path('../tools/Vericairn.DocsTool/workflow-inputs.rb', __dir__), root, commit)
      assert status.success?, error
      document = JSON.parse(output)
      assert_equal [action], document.fetch('allowed')
      assert_equal [4, 5], document.fetch('uses').map { |u| u.fetch('line') }
      assert_equal [action, action], document.fetch('uses').map { |u| u.fetch('action') }
      document.fetch('sources').each do |source|
        assert_equal "sha256:#{Digest::SHA256.file(File.join(root, source.fetch('path'))).hexdigest}", source.fetch('hash')
      end
      File.write(File.join(workflows, 'ci.yml'), "jobs: {build: {uses: example/action@main}}\n")
      output, _error, status = Open3.capture3('ruby', File.expand_path('../tools/Vericairn.DocsTool/workflow-inputs.rb', __dir__), root, commit)
      refute status.success?
      assert_empty output
    end
  end
end
